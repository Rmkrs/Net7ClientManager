namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Net7ClientManager.Observations.Models;

internal sealed class ClientManufacturingLabObserver
{
    private const uint ClientContextManufacturingObjectId = 0x1328;
    private const uint ClientObjectAuxData = 0x88;

    private const uint ModeValue = 0x01ac;
    private const uint ManufactureTargetProperty = 0x0348;
    private const uint ManufactureComponentsProperty = 0x03e4;

    private const uint PropertyVectorBegin = 0x88;
    private const uint PropertyVectorEnd = 0x8c;

    private const uint InventoryItemTemplateIdValue = 0x120;

    private const int TargetSlotCount = 2;
    private const int ComponentSlotCount = 6;

    private readonly ClientObjectResolver objectResolver = new();

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(state);

        state.ProductionRecipe = this.Observe(
            memory,
            state.ClientContextAddress);
    }

    private ClientProductionRecipeObservation Observe(
        ProcessMemoryReader memory,
        uint clientContextAddress)
    {
        if (clientContextAddress == 0)
        {
            return ClientProductionRecipeObservation.Unavailable(
                "SClient is unavailable");
        }

        if (!TryAdd(
                clientContextAddress,
                ClientContextManufacturingObjectId,
                out var manufacturingObjectIdAddress) ||
            !memory.TryReadUInt32(
                manufacturingObjectIdAddress,
                out var manufacturingObjectId))
        {
            return ClientProductionRecipeObservation.Unavailable(
                "Could not read the active ManufacturingLab ObjectId");
        }

        if (manufacturingObjectId == 0 ||
            manufacturingObjectId == uint.MaxValue)
        {
            return ClientProductionRecipeObservation.Unavailable(
                "No ManufacturingLab is active");
        }

        if (!this.objectResolver.TryLookupClientObject(
                memory,
                clientContextAddress,
                manufacturingObjectId,
                out var clientObjectAddress,
                out var resolveError,
                out _))
        {
            return ClientProductionRecipeObservation.Unavailable(
                resolveError,
                manufacturingObjectId);
        }

        if (!TryAdd(
                clientObjectAddress,
                ClientObjectAuxData,
                out var auxDataPointerAddress) ||
            !memory.TryReadUInt32(
                auxDataPointerAddress,
                out var auxDataAddress) ||
            auxDataAddress == 0)
        {
            return ClientProductionRecipeObservation.Unavailable(
                "Could not resolve the ManufacturingLab AuxData pointer",
                manufacturingObjectId,
                clientObjectAddress);
        }

        if (!TryReadFrame(
                memory,
                auxDataAddress,
                out var first,
                out var firstError))
        {
            return ClientProductionRecipeObservation.Unavailable(
                firstError,
                manufacturingObjectId,
                clientObjectAddress,
                auxDataAddress);
        }

        if (!TryReadFrame(
                memory,
                auxDataAddress,
                out var second,
                out var secondError))
        {
            return ClientProductionRecipeObservation.Unavailable(
                secondError,
                manufacturingObjectId,
                clientObjectAddress,
                auxDataAddress);
        }

        if (!string.Equals(
                first.Fingerprint,
                second.Fingerprint,
                StringComparison.Ordinal))
        {
            return ClientProductionRecipeObservation.Unavailable(
                "ManufacturingLab recipe changed while it was being read",
                manufacturingObjectId,
                clientObjectAddress,
                auxDataAddress);
        }

        return new ClientProductionRecipeObservation
        {
            IsAvailable = true,
            Status = string.Create(
                CultureInfo.InvariantCulture,
                $"Available; {second.Kind} recipe {second.OutputItemTemplateId} has {second.Ingredients.Sum(item => item.Quantity)} required unit(s) across {second.Ingredients.Count} ingredient type(s)"),
            ManufacturingObjectId = manufacturingObjectId,
            ClientObjectAddress = clientObjectAddress,
            AuxDataAddress = auxDataAddress,
            Kind = second.Kind,
            OutputItemTemplateId = second.OutputItemTemplateId,
            Ingredients = second.Ingredients,
            RecipeFingerprint = second.Fingerprint,
        };
    }

    private static bool TryReadFrame(
        ProcessMemoryReader memory,
        uint auxDataAddress,
        out RecipeFrame frame,
        out string error)
    {
        frame = null!;
        error = "";

        if (!TryReadInt32(
                memory,
                auxDataAddress,
                ModeValue,
                out var rawMode))
        {
            error = "Could not read ManufacturingLab.Mode";
            return false;
        }

        ClientProductionRecipeKind kind;

        if (rawMode == (int)ClientProductionRecipeKind.Manufacture)
        {
            kind = ClientProductionRecipeKind.Manufacture;
        }
        else if (rawMode == (int)ClientProductionRecipeKind.Refine)
        {
            kind = ClientProductionRecipeKind.Refine;
        }
        else
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"ManufacturingLab mode {rawMode} does not expose a passive manufacture or refining recipe");
            return false;
        }

        if (!TryReadVectorEntries(
                memory,
                auxDataAddress,
                ManufactureTargetProperty,
                TargetSlotCount,
                out var targets,
                out error))
        {
            return false;
        }

        if (!TryReadVectorEntries(
                memory,
                auxDataAddress,
                ManufactureComponentsProperty,
                ComponentSlotCount,
                out var components,
                out error))
        {
            return false;
        }

        var outputItemTemplateId =
            targets.Count == 0
                ? 0
                : ReadPopulatedItemTemplateId(
                    memory,
                    targets[0]);

        if (outputItemTemplateId <= 0)
        {
            error = "The selected manufacturing target is empty";
            return false;
        }

        var ingredientUnits = components
            .Select(address =>
                ReadPopulatedItemTemplateId(memory, address))
            .Where(itemTemplateId => itemTemplateId > 0)
            .ToArray();

        if (ingredientUnits.Length == 0)
        {
            error = "The selected manufacturing recipe has no populated components";
            return false;
        }

        var ingredients = ingredientUnits
            .GroupBy(itemTemplateId => itemTemplateId)
            .Select(group =>
                new ClientProductionRecipeIngredientObservation
                {
                    ItemTemplateId = group.Key,
                    Quantity = group.Count(),
                })
            .OrderBy(ingredient => ingredient.ItemTemplateId)
            .ToArray();

        var fingerprint = ComputeFingerprint(
            kind,
            outputItemTemplateId,
            ingredients);

        frame = new RecipeFrame(
            kind,
            outputItemTemplateId,
            ingredients,
            fingerprint);
        return true;
    }

    private static bool TryReadVectorEntries(
        ProcessMemoryReader memory,
        uint auxDataAddress,
        uint propertyOffset,
        int maximumCount,
        out IReadOnlyList<uint> entries,
        out string error)
    {
        entries = [];
        error = "";

        if (!TryAdd(
                auxDataAddress,
                propertyOffset,
                out var propertyAddress) ||
            !TryAdd(
                propertyAddress,
                PropertyVectorBegin,
                out var beginAddress) ||
            !TryAdd(
                propertyAddress,
                PropertyVectorEnd,
                out var endAddress) ||
            !memory.TryReadUInt32(beginAddress, out var begin) ||
            !memory.TryReadUInt32(endAddress, out var end))
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read ManufacturingLab vector at +0x{propertyOffset:X}");
            return false;
        }

        if (begin == 0 ||
            end < begin ||
            (end - begin) % sizeof(uint) != 0)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"ManufacturingLab vector at +0x{propertyOffset:X} is invalid");
            return false;
        }

        var count = checked((int)((end - begin) / sizeof(uint)));

        if (count is < 1 ||
            count > maximumCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"ManufacturingLab vector at +0x{propertyOffset:X} has unexpected count {count}");
            return false;
        }

        List<uint> result = new(count);

        for (var index = 0; index < count; index++)
        {
            if (!TryAdd(
                    begin,
                    checked((uint)(index * sizeof(uint))),
                    out var entryPointerAddress) ||
                !memory.TryReadUInt32(
                    entryPointerAddress,
                    out var entryAddress) ||
                entryAddress == 0)
            {
                error = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Could not read ManufacturingLab vector entry {index} at +0x{propertyOffset:X}");
                return false;
            }

            result.Add(entryAddress);
        }

        entries = result;
        return true;
    }

    private static int ReadPopulatedItemTemplateId(
        ProcessMemoryReader memory,
        uint entryAddress)
    {
        if (!TryReadInt32(
                memory,
                entryAddress,
                InventoryItemTemplateIdValue,
                out var itemTemplateId))
        {
            return 0;
        }

        return itemTemplateId > 0
            ? itemTemplateId
            : 0;
    }

    private static bool TryReadInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        out int value)
    {
        value = 0;

        if (!TryAdd(
                baseAddress,
                offset,
                out var address) ||
            !memory.TryReadUInt32(
                address,
                out var raw))
        {
            return false;
        }

        value = unchecked((int)raw);
        return true;
    }

    private static string ComputeFingerprint(
        ClientProductionRecipeKind kind,
        int outputItemTemplateId,
        IReadOnlyList<ClientProductionRecipeIngredientObservation> ingredients)
    {
        var source = string.Join(
            "|",
            new[]
            {
                ((int)kind).ToString(CultureInfo.InvariantCulture),
                outputItemTemplateId.ToString(CultureInfo.InvariantCulture),
                string.Join(
                    ",",
                    ingredients
                        .OrderBy(item => item.ItemTemplateId)
                        .Select(item => string.Create(
                            CultureInfo.InvariantCulture,
                            $"{item.ItemTemplateId}:{item.Quantity}"))),
            });

        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
    }

    private static bool TryAdd(
        uint left,
        uint right,
        out uint result)
    {
        try
        {
            result = checked(left + right);
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private sealed record RecipeFrame(
        ClientProductionRecipeKind Kind,
        int OutputItemTemplateId,
        IReadOnlyList<ClientProductionRecipeIngredientObservation> Ingredients,
        string Fingerprint);
}
