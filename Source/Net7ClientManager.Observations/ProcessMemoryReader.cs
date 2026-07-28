using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Net7ClientManager.Observations;

internal sealed class ProcessMemoryReader : IDisposable
{
    private const int MaxRegionChunkSize = 1024 * 1024;

    private readonly SafeProcessHandle handle;

    private ProcessMemoryReader(
        SafeProcessHandle handle)
    {
        this.handle = handle;
    }

    public static ProcessMemoryReader Open(
        int processId)
    {
        const uint processQueryInformation = 0x0400;
        const uint processVmRead = 0x0010;

        var processHandle = ProcessMemoryNativeMethods.OpenProcess(
            processQueryInformation |
            processVmRead,
            inheritHandle: false,
            processId);

        if (processHandle.IsInvalid)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
        }

        return new ProcessMemoryReader(processHandle);
    }

    public static uint GetMainModuleBase(
        Process process)
    {
        return checked(
            (uint)process
                .MainModule!
                .BaseAddress
                .ToInt64());
    }

    public bool TryReadUInt32(
        uint address,
        out uint value)
    {
        value = 0;

        if (!this.TryReadBytes(
                address,
                sizeof(uint),
                out var bytes))
        {
            return false;
        }

        value = BitConverter.ToUInt32(
            bytes,
            0);

        return true;
    }

    public bool TryReadBytes(
        uint address,
        int length,
        out byte[] bytes)
    {
        bytes = new byte[length];

        if (!this.TryReadBytes(
                address,
                bytes))
        {
            bytes = [];
            return false;
        }

        return true;
    }

    public bool TryReadBytes(
        uint address,
        byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer.Length == 0)
        {
            return true;
        }

        if (!ProcessMemoryNativeMethods.ReadProcessMemory(
                this.handle,
                new IntPtr(address),
                buffer,
                new UIntPtr((uint)buffer.Length),
                out var bytesRead))
        {
            return false;
        }

        return bytesRead.ToUInt64() ==
            (ulong)buffer.Length;
    }

    public bool TryReadNullTerminatedLatin1String(
        uint address,
        int maximumLength,
        out string value)
    {
        value = "";

        if (address == 0 ||
            maximumLength <= 0)
        {
            return false;
        }

        const int chunkSize = 32;

        List<byte> collected = [];

        for (var offset = 0;
             offset < maximumLength;
             offset += chunkSize)
        {
            var length = Math.Min(
                chunkSize,
                maximumLength - offset);

            uint chunkAddress;

            try
            {
                chunkAddress = checked(
                    address + (uint)offset);
            }
            catch (OverflowException)
            {
                return false;
            }

            if (!this.TryReadBytes(
                    chunkAddress,
                    length,
                    out var bytes))
            {
                return false;
            }

            var terminatorIndex =
                Array.IndexOf(
                    bytes,
                    (byte)0);

            if (terminatorIndex >= 0)
            {
                collected.AddRange(
                    bytes[..terminatorIndex]);

                value =
                    Encoding.Latin1.GetString([.. collected]);

                return true;
            }

            collected.AddRange(bytes);
        }

        value =
            Encoding.Latin1.GetString([.. collected]);

        return true;
    }

    public IEnumerable<uint> ScanForUInt32(
        uint value)
    {
        var pattern = BitConverter.GetBytes(value);

        uint address = 0x00010000;
        const uint maxAddress = 0x7fff0000;

        while (address < maxAddress)
        {
            if (ProcessMemoryNativeMethods.VirtualQueryEx(
                    this.handle,
                    new IntPtr(address),
                    out var memoryInformation,
                    (uint)Marshal.SizeOf<ProcessMemoryNativeMethods.MemoryBasicInformation>()) == 0)
            {
                address += 0x1000;
                continue;
            }

            var baseAddress = unchecked(
                (uint)memoryInformation
                    .BaseAddress
                    .ToInt64());

            var regionSize = unchecked(
                (uint)memoryInformation
                    .RegionSize
                    .ToUInt64());

            var nextAddress =
                baseAddress +
                Math.Max(
                    regionSize,
                    0x1000);

            if (IsReadableCommitted(
                    memoryInformation))
            {
                foreach (var hit in this.ScanRegion(
                             baseAddress,
                             regionSize,
                             pattern))
                {
                    yield return hit;
                }
            }

            if (nextAddress <= address)
            {
                break;
            }

            address = nextAddress;
        }
    }

    public void Dispose()
    {
        this.handle.Dispose();
    }

    private IEnumerable<uint> ScanRegion(
        uint baseAddress,
        uint regionSize,
        byte[] pattern)
    {
        var remaining = regionSize;
        var offset = 0u;
        byte[] carry = [];

        while (remaining > 0)
        {
            var chunkSize = (int)Math.Min(
                remaining,
                MaxRegionChunkSize);

            if (!this.TryReadBytes(
                    baseAddress + offset,
                    chunkSize,
                    out var chunk))
            {
                yield break;
            }

            var scanBuffer = new byte[
                carry.Length +
                chunk.Length];

            Buffer.BlockCopy(
                carry,
                0,
                scanBuffer,
                0,
                carry.Length);

            Buffer.BlockCopy(
                chunk,
                0,
                scanBuffer,
                carry.Length,
                chunk.Length);

            for (var index = 0;
                 index <=
                 scanBuffer.Length -
                 pattern.Length;
                 index++)
            {
                if (scanBuffer[index] ==
                    pattern[0] &&
                    scanBuffer[index + 1] ==
                    pattern[1] &&
                    scanBuffer[index + 2] ==
                    pattern[2] &&
                    scanBuffer[index + 3] ==
                    pattern[3])
                {
                    yield return
                        baseAddress +
                        offset +
                        (uint)index -
                        (uint)carry.Length;
                }
            }

            var keep = Math.Min(
                pattern.Length - 1,
                scanBuffer.Length);

            carry = new byte[keep];

            Buffer.BlockCopy(
                scanBuffer,
                scanBuffer.Length - keep,
                carry,
                0,
                keep);

            offset += (uint)chunkSize;
            remaining -= (uint)chunkSize;
        }
    }

    private static bool IsReadableCommitted(
        ProcessMemoryNativeMethods.MemoryBasicInformation memoryInformation)
    {
        const uint memCommit = 0x1000;
        const uint pageNoAccess = 0x01;
        const uint pageGuard = 0x100;

        if (memoryInformation.State != memCommit)
        {
            return false;
        }

        if ((memoryInformation.Protect &
             pageNoAccess) != 0 ||
            (memoryInformation.Protect &
             pageGuard) != 0)
        {
            return false;
        }

        var protection =
            memoryInformation.Protect & 0xff;

        return protection is
            0x02 or
            0x04 or
            0x08 or
            0x20 or
            0x40 or
            0x80;
    }
}
