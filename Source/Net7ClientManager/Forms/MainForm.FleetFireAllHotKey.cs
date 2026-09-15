// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Services;

public sealed partial class MainForm
{
    private static readonly FleetCommandDefinition fleetFireAllHotKeyCommand = new()
    {
        Id = "hotkey:fleet-fire-all",
        Label = "Fleet Fire All",
        ShowInOverlay = false,
        Category = FleetCommandCategory.Combat,
        Blocks =
        [
            FleetCommandBlock.For(
                FleetCommandScope.Pilot,
                FleetCommandStep.Action(BuiltInInputActionProvider.FireAllName)),
            FleetCommandBlock.For(
                FleetCommandScope.Followers,
                FleetCommandStep.SetTitle("Assisting {pilot}", durationMilliseconds: 5000),
                FleetCommandStep.TargetInvokingPilotTarget(),
                FleetCommandStep.Delay(100),
                FleetCommandStep.Action(BuiltInInputActionProvider.FireAllName),
                FleetCommandStep.Delay(100)),
            FleetCommandBlock.For(
                FleetCommandScope.System,
                FleetCommandStep.RestorePilotFocus()),
        ],
    };

    private string? fleetFireAllKeyboardHookError;

    private void ApplyFleetFireAllHotKeyRuntimeSettings()
    {
        var hotKey = this.clientManager.FleetCommandSettings.FleetFireAllHotKey;

        if ((hotKey & Keys.KeyCode) == Keys.None)
        {
            this.fleetFireAllKeyboardHook?.Dispose();
            this.fleetFireAllKeyboardHook = null;
            this.fleetFireAllKeyboardHookError = null;
            return;
        }

        if (this.fleetFireAllKeyboardHook != null)
        {
            this.fleetFireAllKeyboardHook.UpdateHotKey(hotKey);
            this.fleetFireAllKeyboardHookError = null;
            return;
        }

        try
        {
            var hook = new CommandPaletteKeyboardHook(
                hotKey,
                this.TryHandleFleetFireAllHotKey,
                hotKeyReleased: static () => { });

            hook.Start();
            this.fleetFireAllKeyboardHook = hook;
            this.fleetFireAllKeyboardHookError = null;
        }
        catch (Win32Exception exception)
        {
            this.fleetFireAllKeyboardHook?.Dispose();
            this.fleetFireAllKeyboardHook = null;
            this.fleetFireAllKeyboardHookError = string.Concat(
                "The Fleet Fire All keyboard hook could not be installed: ",
                exception.Message);
        }
    }

    private bool TryHandleFleetFireAllHotKey()
    {
        var settings = this.clientManager.FleetCommandSettings;
        var hotKey = settings.FleetFireAllHotKey;

        if ((hotKey & Keys.KeyCode) == Keys.None ||
            this.inGameOptionsOpen)
        {
            return false;
        }

        var activeClient = this.clientManager.FindForegroundHostedClient();

        if (activeClient?.LifecycleState != ClientLifecycleState.InGame)
        {
            return false;
        }

        var chatInputState = this.clientManager.ReadChatInputState(
            activeClient.ProcessId,
            out var chatInputStatus);

        if (chatInputState != ClientChatInputState.Inactive)
        {
            if (chatInputState == ClientChatInputState.Unknown)
            {
                Debug.WriteLine(
                    $"Fleet Fire All shortcut passed through because chat input state is unknown: {chatInputStatus}");
            }

            return false;
        }

        if (!this.clientManager.TryGetObservationSnapshot(
                activeClient.ProcessId,
                out var snapshot) ||
            !snapshot.Target.HasTarget)
        {
            return false;
        }

        if (Interlocked.CompareExchange(
                ref this.fleetFireAllHotKeyBusy,
                value: 1,
                comparand: 0) != 0)
        {
            return true;
        }

        try
        {
            this.BeginInvoke(
                () => this.ExecuteFleetFireAllHotKeyAsync(activeClient));
            return true;
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(
                ref this.fleetFireAllHotKeyBusy,
                value: 0);
            return false;
        }
    }

    private async Task ExecuteFleetFireAllHotKeyAsync(
        ClientInstance activeClient)
    {
        try
        {
            if (activeClient.LifecycleState != ClientLifecycleState.InGame)
            {
                return;
            }

            var invocationContext =
                this.CreateInvocationContext(activeClient);

            await this.clientManager
                .ExecuteFleetCommandAsync(
                    fleetFireAllHotKeyCommand,
                    invocationContext)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not
            OutOfMemoryException and not StackOverflowException)
        {
            Debug.WriteLine(
                $"Fleet Fire All shortcut failed: {exception}");
        }
        finally
        {
            Interlocked.Exchange(
                ref this.fleetFireAllHotKeyBusy,
                value: 0);
        }
    }
}
