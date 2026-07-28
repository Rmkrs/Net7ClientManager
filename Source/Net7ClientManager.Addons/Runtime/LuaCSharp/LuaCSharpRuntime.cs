// ReSharper disable StringLiteralTypo
// ReSharper disable CompareOfFloatsByEqualityOperator
namespace Net7ClientManager.Addons.Runtime.LuaCSharp;

using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lua;
using Lua.Standard;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Projection;

internal sealed class LuaCSharpRuntime : ILuaRuntime
{
    private static readonly string[] unsafeBasicGlobals =
    [
        "collectgarbage",
        "dofile",
        "loadfile",
        "load",
        "rawset",
        "setmetatable",
    ];

    private const int MaximumUiWindowCount = 8;
    private const int MaximumUiLabelCount = 64;
    private const int MaximumUiButtonCount = 32;
    private const int MaximumUiWindowMenuItemCount = 16;
    private const int MaximumUiMenuToggleCount = 16;
    private const int MaximumUiWidgetIdLength = 64;
    private const int MaximumUiWindowTitleLength = 128;
    private const int MaximumUiLabelTextLength = 512;
    private const int MaximumUiButtonTextLength = 256;
    private const int MaximumUiButtonTooltipLength = 4096;
    private const int MaximumUiMenuTextLength = 128;
    private const int MaximumUiMenuTooltipLength = 256;
    private const int MaximumUiCoordinate = 4096;
    private const int MaximumUiDimension = 2048;
    private const int MaximumUiPadding = 64;
    private const int MaximumUiCornerRadius = 64;
    private const float MinimumUiFontSize = 6.0f;
    private const float MaximumUiFontSize = 48.0f;

    private const int MaximumStorageKeyLength = 64;
    private const int MaximumStorageDepth = 16;
    private const int MaximumStorageNodeCount = 500000;
    private const int MaximumStorageStringLength = 1024 * 1024;
    private const int MaximumStorageObjectKeyLength = 256;

    private readonly AddonRuntimeOptions options;
    private readonly LuaState state;
    private readonly SemaphoreSlim executionGate = new(initialCount: 1, maxCount: 1);
    private readonly Dictionary<string, List<LuaValue>> eventCallbacks =
        new(comparer: StringComparer.Ordinal);
    private readonly List<LuaValue> loadCallbacks = [];
    private readonly List<LuaValue> unloadCallbacks = [];
    private readonly HashSet<string> uiWindowIds =
        new(comparer: StringComparer.Ordinal);
    private readonly Dictionary<string, string> uiLabelParents =
        new(comparer: StringComparer.Ordinal);
    private readonly Dictionary<string, LuaValue> uiButtonCallbacks =
        new(comparer: StringComparer.Ordinal);
    private readonly Dictionary<string, string> uiButtonParents =
        new(comparer: StringComparer.Ordinal);
    private readonly HashSet<string> uiWindowMenuIds =
        new(comparer: StringComparer.Ordinal);
    private readonly Dictionary<string, LuaValue> uiMenuToggleCallbacks =
        new(comparer: StringComparer.Ordinal);
    private readonly Dictionary<string, string> appliedDomainFingerprints =
        new(comparer: StringComparer.Ordinal);
    private readonly LuaTable gameRootBacking = new();
    private readonly LuaTable gameMetaBacking = new();
    private readonly LuaTable lifecycleBacking = new();
    private readonly LuaTable worldBacking = new();
    private readonly LuaTable characterBacking = new();
    private readonly LuaTable characterIdentityBacking = new();

    private LuaValue readOnlyNewIndexFunction = LuaValue.Nil;
    private LuaValue readOnlyLengthFunction = LuaValue.Nil;
    private LuaValue readOnlyPairsFunction = LuaValue.Nil;
    private LuaValue readOnlyIPairsFunction = LuaValue.Nil;
    private LuaValue readOnlyIPairsIteratorFunction = LuaValue.Nil;
    private LuaTable? characterIdentityProxy;
    private AddonGameSnapshot? latestSnapshot;
    private AddonGameSnapshot? pinnedSnapshot;
    private bool started;
    private bool userGestureActive;
    private bool actionUsedInCurrentGesture;
    private bool disposed;

    public LuaCSharpRuntime(AddonRuntimeOptions options)
    {
        this.options = options;
        this.state = LuaState.Create();
        this.ConfigureSandbox(modules: options.Modules);
    }

    public event EventHandler<AddonLogEntryEventArgs>? LogEntryWritten;

    public event EventHandler<AddonUiCommandEventArgs>? UiCommandEmitted;

    public void UpdateGameSnapshot(AddonGameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(argument: snapshot);
        Volatile.Write(location: ref this.latestSnapshot, value: snapshot);
    }

    public async ValueTask<LuaExecutionResult> StartAsync(
        string source,
        string chunkName,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(condition: this.disposed, instance: this);
        ArgumentNullException.ThrowIfNull(argument: source);

        await this.executionGate
            .WaitAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        try
        {
            if (this.started)
            {
                return new LuaExecutionResult
                {
                    Succeeded = true,
                };
            }

            this.ApplyCurrentGameSnapshot();

            var loadResult = await this.ExecuteSourceCoreAsync(
                    source: source,
                    chunkName: chunkName,
                    executionTimeout: executionTimeout,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            if (!loadResult.Succeeded)
            {
                return loadResult;
            }

            var callbackResult = await this.InvokeCallbacksCoreAsync(
                    callbacks: this.loadCallbacks,
                    arguments: [],
                    operationName: "addon.on_load",
                    executionTimeout: executionTimeout,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            if (callbackResult.Succeeded)
            {
                this.started = true;
            }

            return callbackResult;
        }
        finally
        {
            this.executionGate.Release();
        }
    }

    public async ValueTask<LuaExecutionResult> StopAsync(
        TimeSpan executionTimeout,
        CancellationToken cancellationToken = default)
    {
        if (this.disposed)
        {
            return new LuaExecutionResult
            {
                Succeeded = true,
            };
        }

        await this.executionGate
            .WaitAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        try
        {
            if (!this.started)
            {
                return new LuaExecutionResult
                {
                    Succeeded = true,
                };
            }

            this.ApplyCurrentGameSnapshot();

            var result = await this.InvokeCallbacksCoreAsync(
                    callbacks: this.unloadCallbacks,
                    arguments: [],
                    operationName: "addon.on_unload",
                    executionTimeout: executionTimeout,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            this.started = false;
            return result;
        }
        finally
        {
            this.executionGate.Release();
        }
    }

    public async ValueTask<LuaExecutionResult> ExecuteAsync(
        string source,
        string chunkName,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(condition: this.disposed, instance: this);
        ArgumentNullException.ThrowIfNull(argument: source);

        await this.executionGate
            .WaitAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        try
        {
            this.ApplyCurrentGameSnapshot();

            return await this.ExecuteSourceCoreAsync(
                    source: source,
                    chunkName: chunkName,
                    executionTimeout: executionTimeout,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);
        }
        finally
        {
            this.executionGate.Release();
        }
    }

    public async ValueTask<LuaExecutionResult> RaiseEventAsync(
        AddonGameEvent gameEvent,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(condition: this.disposed, instance: this);
        ArgumentNullException.ThrowIfNull(argument: gameEvent);

        await this.executionGate
            .WaitAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        try
        {
            this.pinnedSnapshot = gameEvent.Snapshot;
            this.ApplyCurrentGameSnapshot();

            if (!this.eventCallbacks.TryGetValue(
                    key: gameEvent.Name,
                    value: out var callbacks) ||
                callbacks.Count == 0)
            {
                return new LuaExecutionResult
                {
                    Succeeded = true,
                };
            }

            var eventTable = new LuaTable
            {
                [key: "name"] = gameEvent.Name,
                [key: "observed_at"] = (gameEvent.OccurredAt ??
                        gameEvent.Snapshot.ObservedAt)
                    .ToUnixTimeMilliseconds(),
            };

            foreach (var item in gameEvent.Data)
            {
                eventTable[key: item.Key] = this.ToLuaValue(value: item.Value);
            }

            var eventValue = this.CreateReadOnlyTable(backing: eventTable);

            return await this.InvokeCallbacksCoreAsync(
                    callbacks: [.. callbacks],
                    arguments: [eventValue],
                    operationName: gameEvent.Name,
                    executionTimeout: executionTimeout,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);
        }
        finally
        {
            this.pinnedSnapshot = null;
            this.ApplyCurrentGameSnapshot();
            this.executionGate.Release();
        }
    }

    public async ValueTask<LuaExecutionResult> RaiseUiInteractionAsync(
        AddonUiInteraction interaction,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(condition: this.disposed, instance: this);
        ArgumentNullException.ThrowIfNull(argument: interaction);

        await this.executionGate
            .WaitAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        try
        {
            this.ApplyCurrentGameSnapshot();

            if (interaction.OwnerProcessId !=
                    this.options.OwnerProcessId ||
                !string.Equals(
                    a: interaction.AddonId,
                    b: this.options.Manifest.Id,
                    comparisonType: StringComparison.Ordinal))
            {
                return new LuaExecutionResult
                {
                    Succeeded = true,
                };
            }

            var callback = LuaValue.Nil;
            LuaTable interactionBacking;
            string operationName;

            switch (interaction.Kind)
            {
                case AddonUiInteractionKind.Click
                    when this.uiButtonCallbacks.TryGetValue(
                        key: interaction.WidgetId,
                        value: out callback):
                    interactionBacking = new LuaTable
                    {
                        [key: "kind"] = "click",
                        [key: "widget_id"] = interaction.WidgetId,
                        [key: "observed_at"] = interaction.ObservedAt
                            .ToUnixTimeMilliseconds(),
                    };
                    operationName = string.Concat(
                        str0: "ui.button click ",
                        str1: interaction.WidgetId);
                    break;

                case AddonUiInteractionKind.MenuToggleChanged
                    when interaction.IsChecked.HasValue &&
                         this.uiMenuToggleCallbacks.TryGetValue(
                             key: interaction.WidgetId,
                             value: out callback):
                    interactionBacking = new LuaTable
                    {
                        [key: "kind"] = "menu_toggle_changed",
                        [key: "widget_id"] = interaction.WidgetId,
                        [key: "checked"] = interaction.IsChecked.Value,
                        [key: "observed_at"] = interaction.ObservedAt
                            .ToUnixTimeMilliseconds(),
                    };
                    operationName = string.Concat(
                        str0: "addon.menu toggle ",
                        str1: interaction.WidgetId);
                    break;

                default:
                    return new LuaExecutionResult
                    {
                        Succeeded = true,
                    };
            }

            this.userGestureActive = true;
            this.actionUsedInCurrentGesture = false;

            var interactionTable = this.CreateReadOnlyTable(
                backing: interactionBacking);

            return await this.InvokeCallbacksCoreAsync(
                    callbacks: [callback],
                    arguments: [interactionTable],
                    operationName: operationName,
                    executionTimeout: executionTimeout,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);
        }
        finally
        {
            this.userGestureActive = false;
            this.actionUsedInCurrentGesture = false;
            this.executionGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return ValueTask.CompletedTask;
        }

        this.disposed = true;
        this.eventCallbacks.Clear();
        this.loadCallbacks.Clear();
        this.unloadCallbacks.Clear();
        this.uiWindowIds.Clear();
        this.uiLabelParents.Clear();
        this.uiButtonCallbacks.Clear();
        this.uiButtonParents.Clear();
        this.uiWindowMenuIds.Clear();
        this.uiMenuToggleCallbacks.Clear();
        this.appliedDomainFingerprints.Clear();
        this.state.Dispose();
        this.executionGate.Dispose();

        return ValueTask.CompletedTask;
    }

    private async ValueTask<LuaExecutionResult> ExecuteSourceCoreAsync(
        string source,
        string chunkName,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken)
    {
        using var executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                token: cancellationToken);

        executionCancellation.CancelAfter(delay: executionTimeout);

        try
        {
            var values = await this.state
                .DoStringAsync(
                    source: source,
                    chunkName: chunkName,
                    cancellationToken: executionCancellation.Token)
                .ConfigureAwait(continueOnCapturedContext: false);

            return new LuaExecutionResult
            {
                Succeeded = true,
                Values = [.. values.Select(selector: FormatValue)],
            };
        }
        catch (OperationCanceledException)
            when (executionCancellation.IsCancellationRequested)
        {
            this.WriteLog(
                level: AddonLogLevel.Warning,
                message: $"Execution cancelled: {chunkName}");

            return new LuaExecutionResult
            {
                WasCancelled = true,
                Error = "Execution cancelled or exceeded its time budget.",
            };
        }
        catch (Exception ex)
        {
            this.WriteLog(
                level: AddonLogLevel.Error,
                message: $"{chunkName}: {ex.Message}");

            return new LuaExecutionResult
            {
                Error = ex.Message,
            };
        }
    }

    private async ValueTask<LuaExecutionResult> InvokeCallbacksCoreAsync(
        IReadOnlyCollection<LuaValue> callbacks,
        LuaValue[] arguments,
        string operationName,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken)
    {
        if (callbacks.Count == 0)
        {
            return new LuaExecutionResult
            {
                Succeeded = true,
            };
        }

        using var executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                token: cancellationToken);

        executionCancellation.CancelAfter(delay: executionTimeout);

        try
        {
            foreach (var callback in callbacks)
            {
                await this.state
                    .CallAsync(
                        function: callback,
                        arguments: arguments,
                        cancellationToken: executionCancellation.Token)
                    .ConfigureAwait(continueOnCapturedContext: false);
            }

            return new LuaExecutionResult
            {
                Succeeded = true,
            };
        }
        catch (OperationCanceledException)
            when (executionCancellation.IsCancellationRequested)
        {
            this.WriteLog(
                level: AddonLogLevel.Warning,
                message: $"Callback cancelled: {operationName}");

            return new LuaExecutionResult
            {
                WasCancelled = true,
                Error = string.Concat(
                    str0: operationName,
                    str1: " exceeded its time budget."),
            };
        }
        catch (Exception ex)
        {
            this.WriteLog(
                level: AddonLogLevel.Error,
                message: $"{operationName}: {ex.Message}");

            return new LuaExecutionResult
            {
                Error = ex.Message,
            };
        }
    }

    private void ConfigureSandbox(
        IReadOnlyDictionary<string, string> modules)
    {
        this.state.OpenBasicLibrary();
        this.state.OpenBitwiseLibrary();
        this.state.OpenMathLibrary();
        this.state.OpenStringLibrary();
        this.state.OpenTableLibrary();
        this.state.OpenModuleLibrary();

        foreach (var name in unsafeBasicGlobals)
        {
            this.state.Environment[key: name] = LuaValue.Nil;
        }

        this.state.Environment[key: "io"] = LuaValue.Nil;
        this.state.Environment[key: "os"] = LuaValue.Nil;
        this.state.Environment[key: "debug"] = LuaValue.Nil;
        this.state.Environment[key: "coroutine"] = LuaValue.Nil;

        this.InitializeTrustedReadOnlyHandler();

        this.state.Environment[key: "print"] =
            new LuaFunction(
                name: "net7_print",
                func: (context, _) =>
                {
                    var message = string.Join(
                        separator: '\t',
                        values: context.Arguments
                            .ToArray()
                            .Select(selector: FormatValue));

                    this.WriteLog(
                        level: AddonLogLevel.Information,
                        message: message);

                    return ValueTask.FromResult(
                        result: context.Return());
                });

        var package = this.state.Environment[key: "package"]
            .Read<LuaTable>();

        package[key: "searchers"] = new LuaTable();
        package[key: "path"] = "";
        package[key: "searchpath"] = LuaValue.Nil;

        this.state.ModuleLoader =
            new InMemoryAddonModuleLoader(modules: modules);

        this.state.Environment[key: "addon"] =
            this.CreateAddonApi();

        this.state.Environment[key: "ui"] =
            this.CreateUiApi();

        this.state.Environment[key: "actions"] =
            this.CreateActionsApi();

        this.InitializeGameApi();
        this.ApplyCurrentGameSnapshot();
    }

    private LuaTable CreateAddonApi()
    {
        var manifest = this.options.Manifest;

        var log = new LuaTable
        {
            [key: "info"] = this.CreateLogFunction(
                level: AddonLogLevel.Information),
            [key: "warn"] = this.CreateLogFunction(
                level: AddonLogLevel.Warning),
            [key: "error"] = this.CreateLogFunction(
                level: AddonLogLevel.Error),
        };

        var menu = new LuaTable
        {
            [key: "register_window"] =
                this.CreateAddonMenuRegisterWindowFunction(),
            [key: "register_toggle"] =
                this.CreateAddonMenuRegisterToggleFunction(),
            [key: "remove"] = this.CreateAddonMenuRemoveFunction(),
        };

        var storage = new LuaTable
        {
            [key: "get"] = this.CreateStorageGetFunction(),
            [key: "set"] = this.CreateStorageSetFunction(),
            [key: "remove"] = this.CreateStorageRemoveFunction(),
            [key: "clear"] = this.CreateStorageClearFunction(),
            [key: "quota_bytes"] = AddonStorageStore.MaximumDocumentBytes,
            [key: "scope"] = "owner",
        };

        var time = new LuaTable
        {
            [key: "format_local"] =
                this.CreateTimeFormatLocalFunction(),
        };

        return this.CreateReadOnlyTable(
            backing: new LuaTable
            {
                [key: "id"] = manifest.Id,
                [key: "name"] = manifest.Name,
                [key: "version"] = manifest.Version,
                [key: "api_version"] = manifest.ApiVersion,
                [key: "log"] = this.CreateReadOnlyTable(backing: log),
                [key: "menu"] = this.CreateReadOnlyTable(backing: menu),
                [key: "storage"] = this.CreateReadOnlyTable(backing: storage),
                [key: "time"] = this.CreateReadOnlyTable(backing: time),
                [key: "on_load"] = this.CreateCallbackRegistrationFunction(
                    name: "net7_addon_on_load",
                    callbacks: this.loadCallbacks),
                [key: "on_unload"] = this.CreateCallbackRegistrationFunction(
                    name: "net7_addon_on_unload",
                    callbacks: this.unloadCallbacks),
            });
    }

    private LuaFunction CreateStorageGetFunction()
    {
        return new LuaFunction(
            name: "net7_addon_storage_get",
            func: async (context, cancellationToken) =>
            {
                if (!TryReadStorageKey(
                        context: context,
                        key: out var key,
                        error: out var keyError))
                {
                    return context.Return(
                        result0: LuaValue.Nil,
                        result1: keyError);
                }

                if (!this.TryGetStorage(
                        storage: out var storage,
                        error: out var storageError))
                {
                    return context.Return(
                        result0: LuaValue.Nil,
                        result1: storageError);
                }

                var result = await storage.ReadAsync(
                        ownerKey: this.options.OwnerKey,
                        addonId: this.options.Manifest.Id,
                        key: key,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);

                if (!result.Succeeded)
                {
                    return context.Return(
                        result0: LuaValue.Nil,
                        result1: result.Error);
                }

                if (!result.Found || result.Value == null)
                {
                    return context.Return(result: LuaValue.Nil);
                }

                return context.Return(
                    result: this.CreateLuaValueFromStorageNode(
                        node: result.Value));
            });
    }

    private LuaFunction CreateStorageSetFunction()
    {
        return new LuaFunction(
            name: "net7_addon_storage_set",
            func: async (context, cancellationToken) =>
            {
                if (!TryReadStorageKey(
                        context: context,
                        key: out var key,
                        error: out var keyError))
                {
                    return context.Return(result0: false, result1: keyError);
                }

                if (!context.HasArgument(index: 1))
                {
                    return context.Return(
                        result0: false,
                        result1: "storage value is required");
                }

                var value = context.GetArgument(index: 1);

                if (value.Type == LuaValueType.Nil)
                {
                    return context.Return(
                        result0: false,
                        result1: "storage value may not be nil; use remove() instead");
                }

                if (!this.TryCreateStorageNode(
                        value: value,
                        node: out var node,
                        error: out var conversionError))
                {
                    return context.Return(
                        result0: false,
                        result1: conversionError);
                }

                if (!this.TryGetStorage(
                        storage: out var storage,
                        error: out var storageError))
                {
                    return context.Return(
                        result0: false,
                        result1: storageError);
                }

                var result = await storage.WriteAsync(
                        ownerKey: this.options.OwnerKey,
                        addonId: this.options.Manifest.Id,
                        key: key,
                        value: node!,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);

                return result.Succeeded
                    ? context.Return(result: true)
                    : context.Return(result0: false, result1: result.Error);
            });
    }

    private LuaFunction CreateStorageRemoveFunction()
    {
        return new LuaFunction(
            name: "net7_addon_storage_remove",
            func: async (context, cancellationToken) =>
            {
                if (!TryReadStorageKey(
                        context: context,
                        key: out var key,
                        error: out var keyError))
                {
                    return context.Return(result0: false, result1: keyError);
                }

                if (!this.TryGetStorage(
                        storage: out var storage,
                        error: out var storageError))
                {
                    return context.Return(
                        result0: false,
                        result1: storageError);
                }

                var result = await storage.RemoveAsync(
                        ownerKey: this.options.OwnerKey,
                        addonId: this.options.Manifest.Id,
                        key: key,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);

                return result.Succeeded
                    ? context.Return(result: true)
                    : context.Return(result0: false, result1: result.Error);
            });
    }

    private LuaFunction CreateStorageClearFunction()
    {
        return new LuaFunction(
            name: "net7_addon_storage_clear",
            func: async (context, cancellationToken) =>
            {
                if (!this.TryGetStorage(
                        storage: out var storage,
                        error: out var storageError))
                {
                    return context.Return(
                        result0: false,
                        result1: storageError);
                }

                var result = await storage.ClearAsync(
                        ownerKey: this.options.OwnerKey,
                        addonId: this.options.Manifest.Id,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);

                return result.Succeeded
                    ? context.Return(result: true)
                    : context.Return(result0: false, result1: result.Error);
            });
    }

    private LuaFunction CreateTimeFormatLocalFunction()
    {
        return new LuaFunction(
            name: "net7_addon_time_format_local",
            func: (context, _) =>
            {
                if (!context.HasArgument(index: 0) ||
                    !context.GetArgument(index: 0)
                        .TryRead<double>(result: out var timestamp) ||
                    !double.IsFinite(d: timestamp))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: LuaValue.Nil,
                            result1: "timestamp must be a finite Unix millisecond number"));
                }

                try
                {
                    var formatted = DateTimeOffset
                        .FromUnixTimeMilliseconds(
                            milliseconds: checked((long)Math.Round(a: timestamp)))
                        .ToLocalTime()
                        .ToString(
                            format: "yyyy-MM-dd HH:mm:ss",
                            formatProvider: CultureInfo.InvariantCulture);

                    return ValueTask.FromResult(
                        result: context.Return(result: formatted));
                }
                catch (Exception ex)
                    when (ex is ArgumentOutOfRangeException or
                          OverflowException)
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: LuaValue.Nil,
                            result1: "timestamp is outside the supported date range"));
                }
            });
    }

    private bool TryGetStorage(
        out AddonStorageStore storage,
        out string error)
    {
        if (this.options.Storage == null ||
            string.IsNullOrWhiteSpace(value: this.options.OwnerKey))
        {
            storage = null!;
            error =
                "persistent storage is unavailable for this addon owner";
            return false;
        }

        storage = this.options.Storage;
        error = "";
        return true;
    }

    private LuaFunction CreateAddonMenuRegisterWindowFunction()
    {
        return new LuaFunction(
            name: "net7_addon_menu_register_window",
            func: (context, _) =>
            {
                if (!TryReadWidgetId(
                        context: context,
                        widgetId: out var widgetId,
                        error: out var widgetError))
                {
                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: widgetError));
                }

                if (!this.uiWindowIds.Contains(item: widgetId))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "window must exist before it can be registered in the Addons menu"));
                }

                if (!context.HasArgument(index: 1) ||
                    !context.GetArgument(index: 1)
                        .TryRead<LuaTable>(result: out var definition))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "menu definition must be a table"));
                }

                if (!TryReadRequiredString(
                        table: definition,
                        key: "text",
                        maximumLength: MaximumUiMenuTextLength,
                        value: out var text,
                        error: out var textError) |
                    !TryReadOptionalText(
                        table: definition,
                        key: "tooltip",
                        maximumLength: MaximumUiMenuTooltipLength,
                        value: out var tooltip,
                        error: out var tooltipError) |
                    !TryReadIntRange(
                        table: definition,
                        key: "order",
                        defaultValue: 0,
                        minimum: -1000,
                        maximum: 1000,
                        value: out var order,
                        error: out var orderError))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: FirstNonEmptyError(
                                fallback: "invalid menu definition",
                                candidates: [textError, tooltipError, orderError])));
                }

                if (!this.uiWindowMenuIds.Contains(item: widgetId) &&
                    this.uiWindowMenuIds.Count >=
                        MaximumUiWindowMenuItemCount)
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: $"an addon may register at most {MaximumUiWindowMenuItemCount} window menu items"));
                }

                this.uiWindowMenuIds.Add(item: widgetId);

                this.EmitUiCommand(
                    command: new AddonUiCommand
                    {
                        Kind = AddonUiCommandKind.RegisterWindowMenuItem,
                        OwnerProcessId = this.options.OwnerProcessId,
                        AddonId = this.options.Manifest.Id,
                        WidgetId = widgetId,
                        WindowMenuItem = new AddonUiWindowMenuItem
                        {
                            AddonName = this.options.Manifest.Name,
                            Text = text,
                            Tooltip = tooltip,
                            Order = order,
                        },
                    });

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private LuaFunction CreateAddonMenuRegisterToggleFunction()
    {
        return new LuaFunction(
            name: "net7_addon_menu_register_toggle",
            func: (context, _) =>
            {
                if (!TryReadWidgetId(
                        context: context,
                        widgetId: out var widgetId,
                        error: out var widgetError))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: widgetError));
                }

                if (!context.HasArgument(index: 1) ||
                    !context.GetArgument(index: 1)
                        .TryRead<LuaTable>(result: out var definition))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "toggle definition must be a table"));
                }

                if (!context.HasArgument(index: 2))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "toggle callback must be a function"));
                }

                var callback = context.GetArgument(index: 2);

                if (callback.Type != LuaValueType.Function)
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "toggle callback must be a function"));
                }

                if (!TryReadRequiredString(
                        table: definition,
                        key: "text",
                        maximumLength: MaximumUiMenuTextLength,
                        value: out var text,
                        error: out var textError) |
                    !TryReadOptionalText(
                        table: definition,
                        key: "tooltip",
                        maximumLength: MaximumUiMenuTooltipLength,
                        value: out var tooltip,
                        error: out var tooltipError) |
                    !TryReadIntRange(
                        table: definition,
                        key: "order",
                        defaultValue: 0,
                        minimum: -1000,
                        maximum: 1000,
                        value: out var order,
                        error: out var orderError) |
                    !TryReadBoolean(
                        table: definition,
                        key: "checked",
                        defaultValue: false,
                        value: out var isChecked,
                        error: out var checkedError))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: FirstNonEmptyError(
                                fallback: "invalid toggle definition",
                                candidates:
                                [
                                    textError,
                                    tooltipError,
                                    orderError,
                                    checkedError,
                                ])));
                }

                if (!this.uiMenuToggleCallbacks.ContainsKey(key: widgetId) &&
                    this.uiMenuToggleCallbacks.Count >=
                        MaximumUiMenuToggleCount)
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: $"an addon may register at most {MaximumUiMenuToggleCount} menu toggles"));
                }

                this.uiMenuToggleCallbacks[key: widgetId] = callback;

                this.EmitUiCommand(
                    command: new AddonUiCommand
                    {
                        Kind = AddonUiCommandKind.RegisterMenuToggle,
                        OwnerProcessId = this.options.OwnerProcessId,
                        AddonId = this.options.Manifest.Id,
                        WidgetId = widgetId,
                        MenuToggle = new AddonUiMenuToggle
                        {
                            AddonName = this.options.Manifest.Name,
                            Text = text,
                            Tooltip = tooltip,
                            Order = order,
                            IsChecked = isChecked,
                        },
                    });

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private LuaFunction CreateAddonMenuRemoveFunction()
    {
        return new LuaFunction(
            name: "net7_addon_menu_remove",
            func: (context, _) =>
            {
                if (!TryReadWidgetId(
                        context: context,
                        widgetId: out var widgetId,
                        error: out var error))
                {
                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: error));
                }

                this.uiWindowMenuIds.Remove(item: widgetId);
                this.uiMenuToggleCallbacks.Remove(key: widgetId);

                this.EmitUiCommand(
                    command: new AddonUiCommand
                    {
                        Kind = AddonUiCommandKind.RemoveMenuItem,
                        OwnerProcessId = this.options.OwnerProcessId,
                        AddonId = this.options.Manifest.Id,
                        WidgetId = widgetId,
                    });

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private LuaTable CreateUiApi()
    {
        var window = new LuaTable
        {
            [key: "set"] = this.CreateUiWindowSetFunction(),
            [key: "show"] = this.CreateUiWindowShowFunction(),
            [key: "set_available"] =
                this.CreateUiWindowSetAvailableFunction(),
            [key: "remove"] = this.CreateUiWindowRemoveFunction(),
        };

        var label = new LuaTable
        {
            [key: "set"] = this.CreateUiLabelSetFunction(),
            [key: "remove"] = this.CreateUiLabelRemoveFunction(),
        };

        var button = new LuaTable
        {
            [key: "set"] = this.CreateUiButtonSetFunction(),
            [key: "remove"] = this.CreateUiButtonRemoveFunction(),
        };

        return this.CreateReadOnlyTable(
            backing: new LuaTable
            {
                [key: "window"] = this.CreateReadOnlyTable(backing: window),
                [key: "label"] = this.CreateReadOnlyTable(backing: label),
                [key: "button"] = this.CreateReadOnlyTable(backing: button),
                [key: "clear"] = this.CreateUiClearFunction(),
            });
    }

    private LuaTable CreateActionsApi()
    {
        var target = new LuaTable
        {
            // Compatibility-only action used by the bundled DPS Meter.
            [key: "select"] = this.CreateTargetSelectActionFunction(),
            [key: "nearest_navigation"] =
                this.CreateGestureActionFunction(
                    kind: AddonActionKind.TargetNearestNavigation,
                    functionName: "net7_actions_target_nearest_navigation"),
            [key: "previous"] =
                this.CreateGestureActionFunction(
                    kind: AddonActionKind.PreviousTarget,
                    functionName: "net7_actions_target_previous"),
        };

        var combat = new LuaTable
        {
            [key: "fire_all"] =
                this.CreateGestureActionFunction(
                    kind: AddonActionKind.FireAllWeapons,
                    functionName: "net7_actions_combat_fire_all"),
        };

        var navigation = new LuaTable
        {
            [key: "open_planner"] =
                this.CreateGestureActionFunction(
                    kind: AddonActionKind.OpenNavigationPlanner,
                    functionName: "net7_actions_navigation_open_planner"),
            [key: "select_next_target"] =
                this.CreateGestureActionFunction(
                    kind: AddonActionKind.SelectNextNavigationTarget,
                    functionName: "net7_actions_navigation_select_next_target"),
            [key: "start_auto_pilot"] =
                this.CreateGestureActionFunction(
                    kind: AddonActionKind.StartNavigationAutoPilot,
                    functionName: "net7_actions_navigation_start_auto_pilot"),
            [key: "stop_auto_pilot"] =
                this.CreateGestureActionFunction(
                    kind: AddonActionKind.StopNavigationAutoPilot,
                    functionName: "net7_actions_navigation_stop_auto_pilot"),
            [key: "clear_route"] =
                this.CreateGestureActionFunction(
                    kind: AddonActionKind.ClearNavigationRoute,
                    functionName: "net7_actions_navigation_clear_route"),
            [key: "plan_return_trip"] =
                this.CreateGestureActionFunction(
                    kind: AddonActionKind.PlanNavigationReturnTrip,
                    functionName: "net7_actions_navigation_plan_return_trip"),
        };

        return this.CreateReadOnlyTable(
            backing: new LuaTable
            {
                [key: "target"] = this.CreateReadOnlyTable(backing: target),
                [key: "combat"] = this.CreateReadOnlyTable(backing: combat),
                [key: "navigation"] =
                    this.CreateReadOnlyTable(backing: navigation),
            });
    }

    private LuaFunction CreateUiWindowSetFunction()
    {
        return new LuaFunction(
            name: "net7_ui_window_set",
            func: (context, _) =>
            {
                if (!TryReadWidgetId(
                        context: context,
                        widgetId: out var widgetId,
                        error: out var widgetError))
                {
                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: widgetError));
                }

                if (!context.HasArgument(index: 1) ||
                    !context.GetArgument(index: 1)
                        .TryRead<LuaTable>(result: out var definition))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "window definition must be a table"));
                }

                if (!TryReadRequiredString(
                        table: definition,
                        key: "title",
                        maximumLength: MaximumUiWindowTitleLength,
                        value: out var title,
                        error: out var titleError) |
                    !TryReadAnchor(
                        table: definition,
                        anchor: out var anchor,
                        error: out var anchorError) |
                    !TryReadCoordinate(
                        table: definition,
                        key: "x",
                        value: out var x,
                        error: out var xError) |
                    !TryReadCoordinate(
                        table: definition,
                        key: "y",
                        value: out var y,
                        error: out var yError) |
                    !TryReadRequiredDimension(
                        table: definition,
                        key: "width",
                        value: out var width,
                        error: out var widthError) |
                    !TryReadRequiredDimension(
                        table: definition,
                        key: "height",
                        value: out var height,
                        error: out var heightError) |
                    !TryReadBoolean(
                        table: definition,
                        key: "visible",
                        defaultValue: true,
                        value: out var visible,
                        error: out var visibleError) |
                    !TryReadBoolean(
                        table: definition,
                        key: "closable",
                        defaultValue: true,
                        value: out var closable,
                        error: out var closableError) |
                    !TryReadBoolean(
                        table: definition,
                        key: "minimizable",
                        defaultValue: true,
                        value: out var minimizable,
                        error: out var minimizableError) |
                    !TryReadBoolean(
                        table: definition,
                        key: "start_minimized",
                        defaultValue: false,
                        value: out var startMinimized,
                        error: out var startMinimizedError) |
                    !TryReadIntRange(
                        table: definition,
                        key: "minimized_width",
                        defaultValue: 0,
                        minimum: 0,
                        maximum: MaximumUiDimension,
                        value: out var minimizedWidth,
                        error: out var minimizedWidthError) |
                    !TryReadIntRange(
                        table: definition,
                        key: "content_padding",
                        defaultValue: 12,
                        minimum: 0,
                        maximum: MaximumUiPadding,
                        value: out var contentPadding,
                        error: out var contentPaddingError) |
                    !TryReadIntRange(
                        table: definition,
                        key: "corner_radius",
                        defaultValue: 8,
                        minimum: 0,
                        maximum: MaximumUiCornerRadius,
                        value: out var cornerRadius,
                        error: out var cornerRadiusError) |
                    !TryReadFloatRange(
                        table: definition,
                        key: "title_font_size",
                        defaultValue: 15.0f,
                        minimum: MinimumUiFontSize,
                        maximum: MaximumUiFontSize,
                        value: out var titleFontSize,
                        error: out var titleFontSizeError) |
                    !TryReadColor(
                        table: definition,
                        key: "background_color",
                        defaultValue: new AddonUiColor(Alpha: 255, Red: 14, Green: 20, Blue: 27),
                        value: out var backgroundColor,
                        error: out var backgroundColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "header_color",
                        defaultValue: new AddonUiColor(Alpha: 255, Red: 18, Green: 29, Blue: 39),
                        value: out var headerColor,
                        error: out var headerColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "border_color",
                        defaultValue: new AddonUiColor(Alpha: 255, Red: 35, Green: 139, Blue: 181),
                        value: out var borderColor,
                        error: out var borderColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "title_color",
                        defaultValue: AddonUiColor.White,
                        value: out var titleColor,
                        error: out var titleColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "chrome_hover_color",
                        defaultValue: new AddonUiColor(Alpha: 255, Red: 53, Green: 97, Blue: 124),
                        value: out var chromeHoverColor,
                        error: out var chromeHoverColorError))
                {
                    var error = FirstNonEmptyError(
                        fallback: "invalid window definition",
                        candidates: [titleError, anchorError, xError, yError, widthError, heightError, visibleError, closableError, minimizableError, startMinimizedError, minimizedWidthError, contentPaddingError, cornerRadiusError, titleFontSizeError, backgroundColorError, headerColorError, borderColorError, titleColorError, chromeHoverColorError]);

                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: error));
                }

                if (!this.uiWindowIds.Contains(item: widgetId) &&
                    this.uiWindowIds.Count >= MaximumUiWindowCount)
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: $"an addon may create at most {MaximumUiWindowCount} windows"));
                }

                this.RemoveLeafWidgetState(widgetId: widgetId);
                this.uiWindowIds.Add(item: widgetId);

                this.EmitUiCommand(
                    command: new AddonUiCommand
                    {
                        Kind = AddonUiCommandKind.UpsertWindow,
                        OwnerProcessId = this.options.OwnerProcessId,
                        AddonId = this.options.Manifest.Id,
                        WidgetId = widgetId,
                        Window = new AddonUiWindow
                        {
                            Title = title,
                            Anchor = anchor,
                            X = x,
                            Y = y,
                            Width = width,
                            Height = height,
                            IsVisible = visible,
                            CanClose = closable,
                            CanMinimize = minimizable,
                            StartMinimized = startMinimized,
                            MinimizedWidth = minimizedWidth,
                            ContentPadding = contentPadding,
                            CornerRadius = cornerRadius,
                            TitleFontSize = titleFontSize,
                            BackgroundColor = backgroundColor,
                            HeaderBackgroundColor = headerColor,
                            BorderColor = borderColor,
                            TitleColor = titleColor,
                            ChromeHoverColor = chromeHoverColor,
                        },
                    });

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private LuaFunction CreateUiWindowShowFunction()
    {
        return new LuaFunction(
            name: "net7_ui_window_show",
            func: (context, _) =>
            {
                if (!TryReadWidgetId(
                        context: context,
                        widgetId: out var widgetId,
                        error: out var error))
                {
                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: error));
                }

                if (!this.uiWindowIds.Contains(item: widgetId))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "window does not exist"));
                }

                this.EmitUiCommand(
                    command: new AddonUiCommand
                    {
                        Kind = AddonUiCommandKind.ShowWindow,
                        OwnerProcessId = this.options.OwnerProcessId,
                        AddonId = this.options.Manifest.Id,
                        WidgetId = widgetId,
                    });

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private LuaFunction CreateUiWindowSetAvailableFunction()
    {
        return new LuaFunction(
            name: "net7_ui_window_set_available",
            func: (context, _) =>
            {
                if (!TryReadWidgetId(
                        context: context,
                        widgetId: out var widgetId,
                        error: out var widgetError))
                {
                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: widgetError));
                }

                if (!this.uiWindowIds.Contains(item: widgetId))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "window does not exist"));
                }

                if (!context.HasArgument(index: 1) ||
                    !context.GetArgument(index: 1).TryRead<bool>(
                        result: out var isAvailable))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "available must be a boolean"));
                }

                var reason = "";

                if (context.HasArgument(index: 2) &&
                    context.GetArgument(index: 2).Type != LuaValueType.Nil)
                {
                    if (!context.GetArgument(index: 2)
                            .TryRead<LuaTable>(result: out var table))
                    {
                        return ValueTask.FromResult(
                            result: context.Return(
                                result0: false,
                                result1: "availability options must be a table"));
                    }

                    if (!TryReadOptionalText(
                            table: table,
                            key: "reason",
                            maximumLength: MaximumUiMenuTooltipLength,
                            value: out var configuredReason,
                            error: out var reasonError))
                    {
                        return ValueTask.FromResult(
                            result: context.Return(result0: false, result1: reasonError!));
                    }

                    reason = configuredReason;
                }

                this.EmitUiCommand(
                    command: new AddonUiCommand
                    {
                        Kind =
                            AddonUiCommandKind.SetWindowAvailability,
                        OwnerProcessId = this.options.OwnerProcessId,
                        AddonId = this.options.Manifest.Id,
                        WidgetId = widgetId,
                        WindowAvailability =
                            new AddonUiWindowAvailability
                            {
                                IsAvailable = isAvailable,
                                Reason = reason,
                            },
                    });

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private LuaFunction CreateUiWindowRemoveFunction()
    {
        return new LuaFunction(
            name: "net7_ui_window_remove",
            func: (context, _) =>
            {
                if (!TryReadWidgetId(
                        context: context,
                        widgetId: out var widgetId,
                        error: out var error))
                {
                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: error));
                }

                this.RemoveWidgetState(widgetId: widgetId);
                this.EmitRemoveWidget(widgetId: widgetId);

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private LuaFunction CreateUiLabelSetFunction()
    {
        return new LuaFunction(
            name: "net7_ui_label_set",
            func: (context, _) =>
            {
                if (!TryReadWidgetId(
                        context: context,
                        widgetId: out var widgetId,
                        error: out var widgetError))
                {
                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: widgetError));
                }

                if (!context.HasArgument(index: 1) ||
                    !context.GetArgument(index: 1)
                        .TryRead<LuaTable>(result: out var definition))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "label definition must be a table"));
                }

                if (!TryReadRequiredString(
                        table: definition,
                        key: "text",
                        maximumLength: MaximumUiLabelTextLength,
                        value: out var text,
                        error: out var textError) |
                    !TryReadOptionalString(
                        table: definition,
                        key: "parent",
                        maximumLength: MaximumUiWidgetIdLength,
                        value: out var parent,
                        error: out var parentError) |
                    !TryReadAnchor(
                        table: definition,
                        anchor: out var anchor,
                        error: out var anchorError) |
                    !TryReadCoordinateSpace(
                        table: definition,
                        coordinateSpace: out var coordinateSpace,
                        error: out var coordinateSpaceError) |
                    !TryReadCoordinate(
                        table: definition,
                        key: "x",
                        value: out var x,
                        error: out var xError) |
                    !TryReadCoordinate(
                        table: definition,
                        key: "y",
                        value: out var y,
                        error: out var yError) |
                    !TryReadDimension(
                        table: definition,
                        key: "width",
                        value: out var width,
                        error: out var widthError) |
                    !TryReadDimension(
                        table: definition,
                        key: "height",
                        value: out var height,
                        error: out var heightError) |
                    !TryReadFloatRange(
                        table: definition,
                        key: "font_size",
                        defaultValue: 10.0f,
                        minimum: MinimumUiFontSize,
                        maximum: MaximumUiFontSize,
                        value: out var fontSize,
                        error: out var fontSizeError) |
                    !TryReadBoolean(
                        table: definition,
                        key: "bold",
                        defaultValue: true,
                        value: out var bold,
                        error: out var boldError) |
                    !TryReadTextAlignment(
                        table: definition,
                        alignment: out var alignment,
                        error: out var alignmentError) |
                    !TryReadIntRange(
                        table: definition,
                        key: "padding",
                        defaultValue: 8,
                        minimum: 0,
                        maximum: MaximumUiPadding,
                        value: out var padding,
                        error: out var paddingError) |
                    !TryReadIntRange(
                        table: definition,
                        key: "corner_radius",
                        defaultValue: 5,
                        minimum: 0,
                        maximum: MaximumUiCornerRadius,
                        value: out var cornerRadius,
                        error: out var cornerRadiusError) |
                    !TryReadColor(
                        table: definition,
                        key: "color",
                        defaultValue: AddonUiColor.White,
                        value: out var textColor,
                        error: out var textColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "background_color",
                        defaultValue: AddonUiColor.DefaultLabelBackground,
                        value: out var backgroundColor,
                        error: out var backgroundColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "border_color",
                        defaultValue: AddonUiColor.DefaultLabelBorder,
                        value: out var borderColor,
                        error: out var borderColorError))
                {
                    var error = FirstNonEmptyError(
                        fallback: "invalid label definition",
                        candidates: [textError, parentError, anchorError, coordinateSpaceError, xError, yError, widthError, heightError, fontSizeError, boldError, alignmentError, paddingError, cornerRadiusError, textColorError, backgroundColorError, borderColorError]);

                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: error));
                }

                if (!string.IsNullOrEmpty(value: parent) &&
                    !this.uiWindowIds.Contains(item: parent))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: $"parent window '{parent}' does not exist"));
                }

                if (!this.uiLabelParents.ContainsKey(key: widgetId) &&
                    this.uiLabelParents.Count >= MaximumUiLabelCount)
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: $"an addon may create at most {MaximumUiLabelCount} labels"));
                }

                this.RemoveWidgetState(widgetId: widgetId);
                this.uiLabelParents[key: widgetId] = parent;

                this.EmitUiCommand(
                    command: new AddonUiCommand
                    {
                        Kind = AddonUiCommandKind.UpsertLabel,
                        OwnerProcessId = this.options.OwnerProcessId,
                        AddonId = this.options.Manifest.Id,
                        WidgetId = widgetId,
                        Label = new AddonUiLabel
                        {
                            Text = text,
                            ParentWidgetId = parent,
                            Anchor = anchor,
                            CoordinateSpace = coordinateSpace,
                            X = x,
                            Y = y,
                            Width = width,
                            Height = height,
                            FontSize = fontSize,
                            IsBold = bold,
                            TextAlignment = alignment,
                            Padding = padding,
                            CornerRadius = cornerRadius,
                            TextColor = textColor,
                            BackgroundColor = backgroundColor,
                            BorderColor = borderColor,
                        },
                    });

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private LuaFunction CreateUiLabelRemoveFunction()
    {
        return new LuaFunction(
            name: "net7_ui_label_remove",
            func: (context, _) =>
            {
                if (!TryReadWidgetId(
                        context: context,
                        widgetId: out var widgetId,
                        error: out var error))
                {
                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: error));
                }

                this.RemoveWidgetState(widgetId: widgetId);
                this.EmitRemoveWidget(widgetId: widgetId);

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private LuaFunction CreateUiButtonSetFunction()
    {
        return new LuaFunction(
            name: "net7_ui_button_set",
            func: (context, _) =>
            {
                if (!TryReadWidgetId(
                        context: context,
                        widgetId: out var widgetId,
                        error: out var widgetError))
                {
                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: widgetError));
                }

                if (!context.HasArgument(index: 1) ||
                    !context.GetArgument(index: 1)
                        .TryRead<LuaTable>(result: out var definition))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "button definition must be a table"));
                }

                if (!context.HasArgument(index: 2))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "button callback must be a function"));
                }

                var callback = context.GetArgument(index: 2);

                if (callback.Type != LuaValueType.Function)
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: "button callback must be a function"));
                }

                if (!TryReadRequiredString(
                        table: definition,
                        key: "text",
                        maximumLength: MaximumUiButtonTextLength,
                        value: out var text,
                        error: out var textError) |
                    !TryReadOptionalText(
                        table: definition,
                        key: "tooltip",
                        maximumLength: MaximumUiButtonTooltipLength,
                        value: out var tooltip,
                        error: out var tooltipError) |
                    !TryReadOptionalString(
                        table: definition,
                        key: "parent",
                        maximumLength: MaximumUiWidgetIdLength,
                        value: out var parent,
                        error: out var parentError) |
                    !TryReadAnchor(
                        table: definition,
                        anchor: out var anchor,
                        error: out var anchorError) |
                    !TryReadCoordinateSpace(
                        table: definition,
                        coordinateSpace: out var coordinateSpace,
                        error: out var coordinateSpaceError) |
                    !TryReadCoordinate(
                        table: definition,
                        key: "x",
                        value: out var x,
                        error: out var xError) |
                    !TryReadCoordinate(
                        table: definition,
                        key: "y",
                        value: out var y,
                        error: out var yError) |
                    !TryReadDimension(
                        table: definition,
                        key: "width",
                        value: out var width,
                        error: out var widthError) |
                    !TryReadDimension(
                        table: definition,
                        key: "height",
                        value: out var height,
                        error: out var heightError) |
                    !TryReadBoolean(
                        table: definition,
                        key: "enabled",
                        defaultValue: true,
                        value: out var enabled,
                        error: out var enabledError) |
                    !TryReadFloatRange(
                        table: definition,
                        key: "font_size",
                        defaultValue: 10.0f,
                        minimum: MinimumUiFontSize,
                        maximum: MaximumUiFontSize,
                        value: out var fontSize,
                        error: out var fontSizeError) |
                    !TryReadBoolean(
                        table: definition,
                        key: "bold",
                        defaultValue: true,
                        value: out var bold,
                        error: out var boldError) |
                    !TryReadTextAlignment(
                        table: definition,
                        alignment: out var alignment,
                        error: out var alignmentError) |
                    !TryReadIntRange(
                        table: definition,
                        key: "padding",
                        defaultValue: 8,
                        minimum: 0,
                        maximum: MaximumUiPadding,
                        value: out var padding,
                        error: out var paddingError) |
                    !TryReadIntRange(
                        table: definition,
                        key: "corner_radius",
                        defaultValue: 5,
                        minimum: 0,
                        maximum: MaximumUiCornerRadius,
                        value: out var cornerRadius,
                        error: out var cornerRadiusError) |
                    !TryReadColor(
                        table: definition,
                        key: "color",
                        defaultValue: AddonUiColor.White,
                        value: out var textColor,
                        error: out var textColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "background_color",
                        defaultValue: AddonUiColor.DefaultButtonBackground,
                        value: out var backgroundColor,
                        error: out var backgroundColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "hover_background_color",
                        defaultValue: AddonUiColor.DefaultButtonHoverBackground,
                        value: out var hoverBackgroundColor,
                        error: out var hoverBackgroundColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "pressed_background_color",
                        defaultValue: AddonUiColor.DefaultButtonPressedBackground,
                        value: out var pressedBackgroundColor,
                        error: out var pressedBackgroundColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "disabled_background_color",
                        defaultValue: AddonUiColor.DefaultButtonDisabledBackground,
                        value: out var disabledBackgroundColor,
                        error: out var disabledBackgroundColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "border_color",
                        defaultValue: AddonUiColor.DefaultButtonBorder,
                        value: out var borderColor,
                        error: out var borderColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "disabled_border_color",
                        defaultValue: AddonUiColor.DefaultButtonDisabledBorder,
                        value: out var disabledBorderColor,
                        error: out var disabledBorderColorError) |
                    !TryReadColor(
                        table: definition,
                        key: "disabled_color",
                        defaultValue: AddonUiColor.DefaultButtonDisabledText,
                        value: out var disabledTextColor,
                        error: out var disabledTextColorError))
                {
                    var error = FirstNonEmptyError(
                        fallback: "invalid button definition",
                        candidates: [textError, tooltipError, parentError, anchorError, coordinateSpaceError, xError, yError, widthError, heightError, enabledError, fontSizeError, boldError, alignmentError, paddingError, cornerRadiusError, textColorError, backgroundColorError, hoverBackgroundColorError, pressedBackgroundColorError, disabledBackgroundColorError, borderColorError, disabledBorderColorError, disabledTextColorError]);

                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: error));
                }

                if (!string.IsNullOrEmpty(value: parent) &&
                    !this.uiWindowIds.Contains(item: parent))
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: $"parent window '{parent}' does not exist"));
                }

                if (!this.uiButtonCallbacks.ContainsKey(key: widgetId) &&
                    this.uiButtonCallbacks.Count >=
                        MaximumUiButtonCount)
                {
                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: false,
                            result1: $"an addon may create at most {MaximumUiButtonCount} buttons"));
                }

                this.RemoveWidgetState(widgetId: widgetId);
                this.uiButtonCallbacks[key: widgetId] = callback;
                this.uiButtonParents[key: widgetId] = parent;

                this.EmitUiCommand(
                    command: new AddonUiCommand
                    {
                        Kind = AddonUiCommandKind.UpsertButton,
                        OwnerProcessId = this.options.OwnerProcessId,
                        AddonId = this.options.Manifest.Id,
                        WidgetId = widgetId,
                        Button = new AddonUiButton
                        {
                            Text = text,
                            Tooltip = tooltip,
                            ParentWidgetId = parent,
                            Anchor = anchor,
                            CoordinateSpace = coordinateSpace,
                            X = x,
                            Y = y,
                            Width = width,
                            Height = height,
                            IsEnabled = enabled,
                            FontSize = fontSize,
                            IsBold = bold,
                            TextAlignment = alignment,
                            Padding = padding,
                            CornerRadius = cornerRadius,
                            TextColor = textColor,
                            BackgroundColor = backgroundColor,
                            HoverBackgroundColor = hoverBackgroundColor,
                            PressedBackgroundColor = pressedBackgroundColor,
                            DisabledBackgroundColor = disabledBackgroundColor,
                            BorderColor = borderColor,
                            DisabledBorderColor = disabledBorderColor,
                            DisabledTextColor = disabledTextColor,
                        },
                    });

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private LuaFunction CreateUiButtonRemoveFunction()
    {
        return new LuaFunction(
            name: "net7_ui_button_remove",
            func: (context, _) =>
            {
                if (!TryReadWidgetId(
                        context: context,
                        widgetId: out var widgetId,
                        error: out var error))
                {
                    return ValueTask.FromResult(
                        result: context.Return(result0: false, result1: error));
                }

                this.RemoveWidgetState(widgetId: widgetId);
                this.EmitRemoveWidget(widgetId: widgetId);

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private LuaFunction CreateTargetSelectActionFunction()
    {
        return new LuaFunction(
            name: "net7_actions_target_select",
            func: async (context, cancellationToken) =>
            {
                if (!this.userGestureActive)
                {
                    return context.Return(
                        result0: false,
                        result1: "actions can only run from an addon button click");
                }

                if (this.actionUsedInCurrentGesture)
                {
                    return context.Return(
                        result0: false,
                        result1: "this button click has already run an action");
                }

                if (!TryReadObjectId(
                        context: context,
                        objectId: out var objectId,
                        error: out var objectIdError))
                {
                    return context.Return(
                        result0: false,
                        result1: objectIdError);
                }

                if (this.options.ActionDispatcher == null)
                {
                    return context.Return(
                        result0: false,
                        result1: "addon actions are unavailable right now");
                }

                var snapshot =
                    Volatile.Read(location: ref this.latestSnapshot);

                this.actionUsedInCurrentGesture = true;

                var result = await this.options.ActionDispatcher(
                        request: new AddonActionRequest
                        {
                            Kind = AddonActionKind.SelectNearbyTarget,
                            OwnerProcessId =
                                this.options.OwnerProcessId,
                            AddonId = this.options.Manifest.Id,
                            ObjectId = objectId,
                            ExpectedSectorId =
                                snapshot?.World.SectorId,
                            SnapshotSequence =
                                snapshot?.Sequence ?? 0,
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(
                        continueOnCapturedContext: false);

                return result.Succeeded
                    ? context.Return(result: true)
                    : context.Return(
                        result0: false,
                        result1: result.Error);
            });
    }

    private LuaFunction CreateGestureActionFunction(
        AddonActionKind kind,
        string functionName)
    {
        return new LuaFunction(
            name: functionName,
            func: async (context, cancellationToken) =>
            {
                if (!this.userGestureActive)
                {
                    return context.Return(
                        result0: false,
                        result1: "actions can only run from an addon button click");
                }

                if (this.actionUsedInCurrentGesture)
                {
                    return context.Return(
                        result0: false,
                        result1: "this button click has already run an action");
                }

                if (this.options.ActionDispatcher == null)
                {
                    return context.Return(
                        result0: false,
                        result1: "addon actions are unavailable in this host context");
                }

                var snapshot =
                    Volatile.Read(location: ref this.latestSnapshot);

                this.actionUsedInCurrentGesture = true;

                var result = await this.options.ActionDispatcher(
                        request: new AddonActionRequest
                        {
                            Kind = kind,
                            OwnerProcessId =
                                this.options.OwnerProcessId,
                            AddonId = this.options.Manifest.Id,
                            ExpectedSectorId =
                                snapshot?.World.SectorId,
                            SnapshotSequence =
                                snapshot?.Sequence ?? 0,
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);

                return result.Succeeded
                    ? context.Return(result: true)
                    : context.Return(result0: false, result1: result.Error);
            });
    }

    private LuaFunction CreateUiClearFunction()
    {
        return new LuaFunction(
            name: "net7_ui_clear",
            func: (context, _) =>
            {
                this.uiWindowIds.Clear();
                this.uiLabelParents.Clear();
                this.uiButtonCallbacks.Clear();
                this.uiButtonParents.Clear();
                this.uiWindowMenuIds.Clear();
                this.uiMenuToggleCallbacks.Clear();

                this.EmitUiCommand(
                    command: new AddonUiCommand
                    {
                        Kind = AddonUiCommandKind.ClearAddon,
                        OwnerProcessId = this.options.OwnerProcessId,
                        AddonId = this.options.Manifest.Id,
                    });

                return ValueTask.FromResult(
                    result: context.Return(result: true));
            });
    }

    private void RemoveLeafWidgetState(string widgetId)
    {
        this.uiLabelParents.Remove(key: widgetId);
        this.uiButtonCallbacks.Remove(key: widgetId);
        this.uiButtonParents.Remove(key: widgetId);
    }

    private void RemoveWidgetState(string widgetId)
    {
        var removedWindow = this.uiWindowIds.Remove(item: widgetId);
        this.uiWindowMenuIds.Remove(item: widgetId);
        this.RemoveLeafWidgetState(widgetId: widgetId);

        if (!removedWindow)
        {
            return;
        }

        foreach (var childId in this.uiLabelParents
                     .Where(predicate: item => string.Equals(
                         a: item.Value,
                         b: widgetId,
                         comparisonType: StringComparison.Ordinal))
                     .Select(selector: item => item.Key)
                     .ToArray())
        {
            this.uiLabelParents.Remove(key: childId);
        }

        foreach (var childId in this.uiButtonParents
                     .Where(predicate: item => string.Equals(
                         a: item.Value,
                         b: widgetId,
                         comparisonType: StringComparison.Ordinal))
                     .Select(selector: item => item.Key)
                     .ToArray())
        {
            this.uiButtonParents.Remove(key: childId);
            this.uiButtonCallbacks.Remove(key: childId);
        }
    }

    private void EmitRemoveWidget(string widgetId)
    {
        this.EmitUiCommand(
            command: new AddonUiCommand
            {
                Kind = AddonUiCommandKind.RemoveWidget,
                OwnerProcessId = this.options.OwnerProcessId,
                AddonId = this.options.Manifest.Id,
                WidgetId = widgetId,
            });
    }

    private void EmitUiCommand(AddonUiCommand command)
    {
        this.UiCommandEmitted?.Invoke(
            sender: this,
            e: new AddonUiCommandEventArgs(command: command));
    }

    private static bool TryReadStorageKey(
        LuaFunctionExecutionContext context,
        out string key,
        out string error)
    {
        key = "";
        error = "";

        if (!context.HasArgument(index: 0) ||
            !context.GetArgument(index: 0).TryRead<string>(result: out var value) ||
            string.IsNullOrWhiteSpace(value: value))
        {
            error = "storage key must be a non-empty string";
            return false;
        }

        value = value.Trim();

        if (value.Length > MaximumStorageKeyLength ||
            value.Any(predicate: character =>
                !char.IsAsciiLetterOrDigit(c: character) &&
                character != '.' &&
                character != '_' &&
                character != '-'))
        {
            error =
                "storage key may contain only ASCII letters, digits, '.', '_' and '-'";
            return false;
        }

        key = value;
        return true;
    }

    private static bool TryReadWidgetId(
        LuaFunctionExecutionContext context,
        out string widgetId,
        out string error)
    {
        widgetId = "";
        error = "";

        if (!context.HasArgument(index: 0) ||
            !context.GetArgument(index: 0).TryRead<string>(result: out var value) ||
            string.IsNullOrWhiteSpace(value: value))
        {
            error = "widget id must be a non-empty string";
            return false;
        }

        value = value.Trim();

        if (value.Length > MaximumUiWidgetIdLength ||
            value.Any(predicate: character =>
                !char.IsAsciiLetterOrDigit(c: character) &&
                character != '_' && character != '-'))
        {
            error =
                "widget id may contain only ASCII letters, digits, '_' and '-'";
            return false;
        }

        widgetId = value;
        return true;
    }

    private static string FirstNonEmptyError(
        string fallback,
        params string?[] candidates)
    {
        return candidates.FirstOrDefault(
                   predicate: candidate =>
                       !string.IsNullOrWhiteSpace(value: candidate)) ??
               fallback;
    }

    private static bool TryReadRequiredString(
        LuaTable table,
        string key,
        int maximumLength,
        out string value,
        out string error)
    {
        value = "";
        error = "";

        if (!table[key: key].TryRead<string>(result: out var candidate) ||
            string.IsNullOrWhiteSpace(value: candidate))
        {
            error = $"{key} must be a non-empty string";
            return false;
        }

        candidate = candidate.Trim();

        if (candidate.Length > maximumLength)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"{key} may not exceed {maximumLength} characters");
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool TryReadOptionalText(
        LuaTable table,
        string key,
        int maximumLength,
        out string value,
        out string? error)
    {
        value = "";
        error = null;

        var luaValue = table[key: key];

        if (luaValue.Type == LuaValueType.Nil)
        {
            return true;
        }

        if (!luaValue.TryRead<string>(result: out var candidate))
        {
            error = $"{key} must be a string";
            return false;
        }

        candidate = candidate.Trim();

        if (candidate.Length > maximumLength)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"{key} may not exceed {maximumLength} characters");
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool TryReadOptionalString(
        LuaTable table,
        string key,
        int maximumLength,
        out string value,
        out string? error)
    {
        value = "";
        error = null;

        var luaValue = table[key: key];

        if (luaValue.Type == LuaValueType.Nil)
        {
            return true;
        }

        if (!luaValue.TryRead<string>(result: out var candidate))
        {
            error = $"{key} must be a string";
            return false;
        }

        candidate = candidate.Trim();

        if (candidate.Length > maximumLength)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"{key} may not exceed {maximumLength} characters");
            return false;
        }

        if (candidate.Any(predicate: character =>
                !char.IsAsciiLetterOrDigit(c: character) &&
                character != '_' && character != '-'))
        {
            error =
                $"{key} may contain only ASCII letters, digits, '_' and '-'";
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool TryReadCoordinate(
        LuaTable table,
        string key,
        out int value,
        out string error)
    {
        value = 0;
        error = "";

        var luaValue = table[key: key];

        if (luaValue.Type == LuaValueType.Nil)
        {
            return true;
        }

        if (!luaValue.TryRead<double>(result: out var candidate) ||
            !double.IsFinite(d: candidate))
        {
            error = $"{key} must be a finite number";
            return false;
        }

        var bounded = Math.Clamp(
            value: Math.Round(a: candidate),
            min: -MaximumUiCoordinate,
            max: MaximumUiCoordinate);

        value = (int)bounded;

        return true;
    }

    private static bool TryReadDimension(
        LuaTable table,
        string key,
        out int value,
        out string? error)
    {
        value = 0;
        error = null;

        var luaValue = table[key: key];

        if (luaValue.Type == LuaValueType.Nil)
        {
            return true;
        }

        if (!luaValue.TryRead<double>(result: out var candidate) ||
            !double.IsFinite(d: candidate) ||
            candidate < 1 ||
            candidate > MaximumUiDimension)
        {
            error =
                $"{key} must be between 1 and {MaximumUiDimension}";

            return false;
        }

        value = (int)Math.Round(a: candidate);
        return true;
    }

    private static bool TryReadBoolean(
        LuaTable table,
        string key,
        bool defaultValue,
        out bool value,
        out string? error)
    {
        value = defaultValue;
        error = null;

        var luaValue = table[key: key];

        if (luaValue.Type == LuaValueType.Nil)
        {
            return true;
        }

        if (!luaValue.TryRead(result: out value))
        {
            error = $"{key} must be a boolean";
            return false;
        }

        return true;
    }

    private static bool TryReadRequiredDimension(
        LuaTable table,
        string key,
        out int value,
        out string? error)
    {
        if (!TryReadDimension(
                table: table,
                key: key,
                value: out value,
                error: out error))
        {
            return false;
        }

        if (value > 0)
        {
            return true;
        }

        error = $"{key} is required";
        return false;
    }

    private static bool TryReadIntRange(
        LuaTable table,
        string key,
        int defaultValue,
        int minimum,
        int maximum,
        out int value,
        out string? error)
    {
        value = defaultValue;
        error = null;

        var luaValue = table[key: key];

        if (luaValue.Type == LuaValueType.Nil)
        {
            return true;
        }

        if (!luaValue.TryRead<double>(result: out var candidate) ||
            !double.IsFinite(d: candidate) ||
            candidate < minimum ||
            candidate > maximum)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"{key} must be between {minimum} and {maximum}");
            return false;
        }

        value = (int)Math.Round(a: candidate);
        return true;
    }

    private static bool TryReadFloatRange(
        LuaTable table,
        string key,
        float defaultValue,
        float minimum,
        float maximum,
        out float value,
        out string? error)
    {
        value = defaultValue;
        error = null;

        var luaValue = table[key: key];

        if (luaValue.Type == LuaValueType.Nil)
        {
            return true;
        }

        if (!luaValue.TryRead<double>(result: out var candidate) ||
            !double.IsFinite(d: candidate) ||
            candidate < minimum ||
            candidate > maximum)
        {
            error = string.Create(CultureInfo.InvariantCulture, $"{key} must be between {minimum} and {maximum}");
            return false;
        }

        value = (float)candidate;
        return true;
    }

    private static bool TryReadTextAlignment(
        LuaTable table,
        out AddonUiTextAlignment alignment,
        out string? error)
    {
        alignment = AddonUiTextAlignment.Left;
        error = null;

        var luaValue = table[key: "align"];

        if (luaValue.Type == LuaValueType.Nil)
        {
            return true;
        }

        if (!luaValue.TryRead<string>(result: out var candidate))
        {
            error = "align must be a string";
            return false;
        }

        alignment = candidate switch
        {
            "left" => AddonUiTextAlignment.Left,
            "center" => AddonUiTextAlignment.Center,
            "right" => AddonUiTextAlignment.Right,
            _ => (AddonUiTextAlignment)(-1),
        };

        if ((int)alignment >= 0)
        {
            return true;
        }

        error = "align must be left, center or right";
        return false;
    }

    private static bool TryReadColor(
        LuaTable table,
        string key,
        AddonUiColor defaultValue,
        out AddonUiColor value,
        out string? error)
    {
        value = defaultValue;
        error = null;

        var luaValue = table[key: key];

        if (luaValue.Type == LuaValueType.Nil)
        {
            return true;
        }

        if (!luaValue.TryRead<string>(result: out var candidate))
        {
            error = $"{key} must be a color string";
            return false;
        }

        candidate = candidate.Trim();

        if (string.Equals(
                a: candidate,
                b: "transparent",
                comparisonType: StringComparison.OrdinalIgnoreCase))
        {
            value = AddonUiColor.Transparent;
            return true;
        }

        if (!candidate.StartsWith(value: '#') ||
            candidate.Length is not (7 or 9))
        {
            error =
                $"{key} must use #RRGGBB, #AARRGGBB or transparent";
            return false;
        }

        var hex = candidate.AsSpan(start: 1);

        if (!uint.TryParse(
                s: hex,
                style: NumberStyles.HexNumber,
                provider: CultureInfo.InvariantCulture,
                result: out var raw))
        {
            error = $"{key} contains invalid hexadecimal digits";
            return false;
        }

        if (hex.Length == 6)
        {
            value = new AddonUiColor(
                Alpha: 255,
                Red: (byte)(raw >> 16),
                Green: (byte)(raw >> 8),
                Blue: (byte)raw);
        }
        else
        {
            value = new AddonUiColor(
                Alpha: (byte)(raw >> 24),
                Red: (byte)(raw >> 16),
                Green: (byte)(raw >> 8),
                Blue: (byte)raw);
        }

        return true;
    }

    private static bool TryReadObjectId(
        LuaFunctionExecutionContext context,
        out uint objectId,
        out string error)
    {
        objectId = 0;
        error = "";

        if (!context.HasArgument(index: 0) ||
            !context.GetArgument(index: 0)
                .TryRead<double>(result: out var value) ||
            !double.IsFinite(value) ||
            value < 1 ||
            value > uint.MaxValue ||
            Math.Truncate(value) != value)
        {
            error = "the selected target is no longer available";
            return false;
        }

        objectId = checked((uint)value);
        return true;
    }

    private static bool TryReadCoordinateSpace(
        LuaTable table,
        out AddonUiCoordinateSpace coordinateSpace,
        out string error)
    {
        coordinateSpace = AddonUiCoordinateSpace.Pixels;
        error = "";

        var luaValue = table[key: "coordinate_space"];

        if (luaValue.Type == LuaValueType.Nil)
        {
            return true;
        }

        if (!luaValue.TryRead<string>(result: out var candidate))
        {
            error = "coordinate_space must be a string";
            return false;
        }

        coordinateSpace = candidate switch
        {
            "pixels" => AddonUiCoordinateSpace.Pixels,
            "game_canvas" => AddonUiCoordinateSpace.GameCanvas,
            _ => (AddonUiCoordinateSpace)(-1),
        };

        if ((int)coordinateSpace >= 0)
        {
            return true;
        }

        error = "coordinate_space must be pixels or game_canvas";
        return false;
    }

    private static bool TryReadAnchor(
        LuaTable table,
        out AddonUiAnchor anchor,
        out string error)
    {
        anchor = AddonUiAnchor.TopLeft;
        error = "";

        var luaValue = table[key: "anchor"];

        if (luaValue.Type == LuaValueType.Nil)
        {
            return true;
        }

        if (!luaValue.TryRead<string>(result: out var candidate))
        {
            error = "anchor must be a string";
            return false;
        }

        anchor = candidate switch
        {
            "top_left" => AddonUiAnchor.TopLeft,
            "top_center" => AddonUiAnchor.TopCenter,
            "top_right" => AddonUiAnchor.TopRight,
            "center_left" => AddonUiAnchor.CenterLeft,
            "center" => AddonUiAnchor.Center,
            "center_right" => AddonUiAnchor.CenterRight,
            "bottom_left" => AddonUiAnchor.BottomLeft,
            "bottom_center" => AddonUiAnchor.BottomCenter,
            "bottom_right" => AddonUiAnchor.BottomRight,
            _ => (AddonUiAnchor)(-1),
        };

        if ((int)anchor >= 0)
        {
            return true;
        }

        error =
            "anchor must be top_left, top_center, top_right, center_left, center, center_right, bottom_left, bottom_center or bottom_right";
        return false;
    }

    private LuaFunction CreateLogFunction(AddonLogLevel level)
    {
        return new LuaFunction(
            name: string.Concat(arg0: "net7_log_", arg1: level),
            func: (context, _) =>
            {
                var message = string.Join(
                    separator: '\t',
                    values: context.Arguments
                        .ToArray()
                        .Select(selector: FormatValue));

                this.WriteLog(level: level, message: message);

                return ValueTask.FromResult(
                    result: context.Return());
            });
    }

    private LuaFunction CreateCallbackRegistrationFunction(
        string name,
        ICollection<LuaValue> callbacks)
    {
        return new LuaFunction(
            name: name,
            func: (context, _) =>
            {
                var callback = context.GetArgument(index: 0);

                if (callback.Type != LuaValueType.Function)
                {
                    throw new LuaRuntimeException(
                        state: context.State,
                        errorObject: "callback must be a function");
                }

                callbacks.Add(item: callback);

                return ValueTask.FromResult(
                    result: context.Return());
            });
    }

    private void InitializeGameApi()
    {
        this.characterIdentityProxy =
            this.CreateReadOnlyTable(backing: this.characterIdentityBacking);

        var events = this.CreateReadOnlyTable(
            backing: new LuaTable
            {
                [key: "on"] = new LuaFunction(
                    name: "net7_events_on",
                    func: (context, _) =>
                    {
                        var eventName =
                            context.GetArgument<string>(index: 0);

                        var callback =
                            context.GetArgument(index: 1);

                        if (callback.Type != LuaValueType.Function)
                        {
                            throw new LuaRuntimeException(
                                state: context.State,
                                errorObject: "event callback must be a function");
                        }

                        if (!this.eventCallbacks.TryGetValue(
                                key: eventName,
                                value: out var callbacks))
                        {
                            callbacks = [];
                            this.eventCallbacks[key: eventName] = callbacks;
                        }

                        callbacks.Add(item: callback);

                        return ValueTask.FromResult(
                            result: context.Return());
                    }),
            });

        this.gameRootBacking[key: "meta"] =
            this.CreateReadOnlyTable(backing: this.gameMetaBacking);
        this.gameRootBacking[key: "lifecycle"] =
            this.CreateReadOnlyTable(backing: this.lifecycleBacking);
        this.gameRootBacking[key: "world"] =
            this.CreateReadOnlyTable(backing: this.worldBacking);
        this.gameRootBacking[key: "character"] =
            this.CreateReadOnlyTable(backing: this.characterBacking);
        this.gameRootBacking[key: "events"] = events;

        this.state.Environment[key: "game"] =
            this.CreateReadOnlyTable(backing: this.gameRootBacking);
    }

    private void ApplyCurrentGameSnapshot()
    {
        var snapshot =
            this.pinnedSnapshot ??
            Volatile.Read(location: ref this.latestSnapshot) ??
            GameSnapshotProjector.CreateSynthetic(
                lifecycleState: "unknown",
                characterName: "Unknown");

        this.gameMetaBacking[key: "api_version"] =
            AddonApiVersion.Current;
        this.gameMetaBacking[key: "observed_at"] = snapshot.ObservedAt
            .ToUnixTimeMilliseconds();

        this.lifecycleBacking[key: "state"] = snapshot.Lifecycle.State;
        this.lifecycleBacking[key: "is_in_game"] = snapshot.Lifecycle.IsInGame;
        this.lifecycleBacking[key: "is_transitioning"] =
            snapshot.Lifecycle.IsTransitioning;

        this.worldBacking[key: "available"] = snapshot.World.IsAvailable;
        this.worldBacking[key: "environment"] = snapshot.World.Environment;
        this.worldBacking[key: "system_name"] =
            ToLuaValue(value: snapshot.World.SystemName);
        this.worldBacking[key: "sector_name"] =
            ToLuaValue(value: snapshot.World.SectorName);
        this.worldBacking[key: "starbase_name"] =
            ToLuaValue(value: snapshot.World.StarbaseName);
        // Compatibility-only field used by the bundled DPS addons.
        this.worldBacking[key: "sector_id"] =
            snapshot.World.SectorId ?? LuaValue.Nil;

        this.characterBacking[key: "available"] =
            snapshot.Character.IsAvailable;

        if (snapshot.Character.Identity is { } identity)
        {
            this.characterIdentityBacking[key: "name"] =
                ToLuaValue(value: identity.Name);
            this.characterIdentityBacking[key: "title"] =
                ToLuaValue(value: identity.Title);
            this.characterIdentityBacking[key: "rank"] =
                ToLuaValue(value: identity.Rank);
            this.characterIdentityBacking[key: "race"] =
                ToLuaValue(value: identity.Race);
            this.characterIdentityBacking[key: "profession"] =
                ToLuaValue(value: identity.Profession);
            this.characterIdentityBacking[key: "affiliation"] =
                ToLuaValue(value: identity.Affiliation);
            this.characterIdentityBacking[key: "guild_name"] =
                ToLuaValue(value: identity.GuildName);
            this.characterIdentityBacking[key: "guild_rank"] =
                ToLuaValue(value: identity.GuildRank);
            this.characterIdentityBacking[key: "combat_level"] =
                identity.CombatLevel ?? LuaValue.Nil;

            this.characterBacking[key: "identity"] =
                this.characterIdentityProxy!;
        }
        else
        {
            this.characterIdentityBacking[key: "name"] = LuaValue.Nil;
            this.characterIdentityBacking[key: "title"] = LuaValue.Nil;
            this.characterIdentityBacking[key: "rank"] = LuaValue.Nil;
            this.characterIdentityBacking[key: "race"] = LuaValue.Nil;
            this.characterIdentityBacking[key: "profession"] = LuaValue.Nil;
            this.characterIdentityBacking[key: "affiliation"] = LuaValue.Nil;
            this.characterIdentityBacking[key: "guild_name"] = LuaValue.Nil;
            this.characterIdentityBacking[key: "guild_rank"] = LuaValue.Nil;
            this.characterIdentityBacking[key: "combat_level"] = LuaValue.Nil;
            this.characterBacking[key: "identity"] = LuaValue.Nil;
        }

        this.ApplyPublicData(snapshot: snapshot);
    }

    private void ApplyPublicData(AddonGameSnapshot snapshot)
    {
        foreach (var domain in snapshot.PublicData)
        {
            var hasFingerprint =
                snapshot.DomainFingerprints.TryGetValue(
                    key: domain.Key,
                    value: out var fingerprint);

            if (hasFingerprint &&
                this.appliedDomainFingerprints.TryGetValue(
                    key: domain.Key,
                    value: out var appliedFingerprint) &&
                string.Equals(
                    a: fingerprint,
                    b: appliedFingerprint,
                    comparisonType: StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(
                    a: domain.Key,
                    b: "character",
                    comparisonType: StringComparison.Ordinal) &&
                domain.Value is
                    IReadOnlyDictionary<string, object?>
                    characterData)
            {
                foreach (var item in characterData)
                {
                    if (item.Key is "available" or "identity")
                    {
                        continue;
                    }

                    this.characterBacking[key: item.Key] =
                        this.ToLuaValue(value: item.Value);
                }
            }
            else
            {
                this.gameRootBacking[key: domain.Key] =
                    this.ToLuaValue(value: domain.Value);
            }

            if (hasFingerprint)
            {
                this.appliedDomainFingerprints[key: domain.Key] = fingerprint!;
            }
            else
            {
                this.appliedDomainFingerprints.Remove(
                    key: domain.Key);
            }
        }
    }

    private void InitializeTrustedReadOnlyHandler()
    {
        this.readOnlyNewIndexFunction = this.state.Load(
            chunk: "error('Net7 API tables are read-only', 2)",
            chunkName: "@net7/readonly-bootstrap.lua");

        this.readOnlyLengthFunction =
            new LuaFunction(
                name: "net7_readonly_length",
                func: (context, _) =>
                {
                    var backing =
                        GetReadOnlyBacking(context: context);

                    return ValueTask.FromResult(
                        result: context.Return(result: backing.ArrayLength));
                });

        this.readOnlyPairsFunction =
            new LuaFunction(
                name: "net7_readonly_pairs",
                func: (context, _) =>
                {
                    var backing =
                        GetReadOnlyBacking(context: context);

                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: this.state.Environment[key: "next"],
                            result1: backing,
                            result2: LuaValue.Nil));
                });

        this.readOnlyIPairsIteratorFunction =
            new LuaFunction(
                name: "net7_readonly_ipairs_iterator",
                func: (context, _) =>
                {
                    var backing =
                        context.GetArgument<LuaTable>(index: 0);

                    var index =
                        context.GetArgument<int>(index: 1) + 1;

                    var value = backing[key: index];

                    return value.Type == LuaValueType.Nil
                        ? ValueTask.FromResult(
                            result: context.Return(
                                result0: LuaValue.Nil,
                                result1: LuaValue.Nil))
                        : ValueTask.FromResult(
                            result: context.Return(result0: index, result1: value));
                });

        this.readOnlyIPairsFunction =
            new LuaFunction(
                name: "net7_readonly_ipairs",
                func: (context, _) =>
                {
                    var backing =
                        GetReadOnlyBacking(context: context);

                    return ValueTask.FromResult(
                        result: context.Return(
                            result0: this.readOnlyIPairsIteratorFunction,
                            result1: backing,
                            result2: 0));
                });
    }

    private LuaTable CreateReadOnlyTable(LuaTable backing)
    {
        var proxy = new LuaTable
        {
            Metatable = new LuaTable
            {
                [key: "__index"] = backing,
                [key: "__newindex"] = this.readOnlyNewIndexFunction,
                [key: "__len"] = this.readOnlyLengthFunction,
                [key: "__pairs"] = this.readOnlyPairsFunction,
                [key: "__ipairs"] = this.readOnlyIPairsFunction,
                [key: "__net7_backing"] = backing,
                [key: "__metatable"] = "locked",
            },
        };

        return proxy;
    }

    private static LuaTable GetReadOnlyBacking(
        LuaFunctionExecutionContext context)
    {
        var proxy = context.GetArgument<LuaTable>(index: 0);
        var metatable = proxy.Metatable;

        if (metatable == null ||
            !metatable[key: "__net7_backing"]
                .TryRead<LuaTable>(result: out var backing))
        {
            throw new LuaRuntimeException(
                state: context.State,
                errorObject: "Net7 read-only table backing is unavailable");
        }

        return backing;
    }

    private bool TryCreateStorageNode(
        LuaValue value,
        out JsonNode? node,
        out string error)
    {
        var activeTables = new HashSet<LuaTable>(
            comparer: ReferenceEqualityComparer.Instance);
        var nodeCount = 0;

        return this.TryCreateStorageNodeCore(
            value: value,
            depth: 0,
            activeTables: activeTables,
            nodeCount: ref nodeCount,
            node: out node,
            error: out error);
    }

    private bool TryCreateStorageNodeCore(
        LuaValue value,
        int depth,
        HashSet<LuaTable> activeTables,
        ref int nodeCount,
        out JsonNode? node,
        out string error)
    {
        node = null;
        error = "";

        nodeCount++;

        if (nodeCount > MaximumStorageNodeCount)
        {
            error =
                $"storage value may contain at most {MaximumStorageNodeCount} values";
            return false;
        }

        if (depth > MaximumStorageDepth)
        {
            error =
                $"storage value may be nested at most {MaximumStorageDepth} levels";
            return false;
        }

        if (value.TryRead<bool>(result: out var boolean))
        {
            node = JsonValue.Create(value: boolean);
            return true;
        }

        if (value.TryRead<string>(result: out var text))
        {
            if (text.Length > MaximumStorageStringLength)
            {
                error =
                    $"storage strings may not exceed {MaximumStorageStringLength} characters";
                return false;
            }

            node = JsonValue.Create(value: text);
            return true;
        }

        if (value.TryRead<double>(result: out var number))
        {
            if (!double.IsFinite(d: number))
            {
                error =
                    "storage numbers must be finite JSON numbers";
                return false;
            }

            node = JsonValue.Create(value: number);
            return true;
        }

        if (!value.TryRead<LuaTable>(result: out var table))
        {
            error =
                "storage values may contain only booleans, strings, finite numbers and plain tables";
            return false;
        }

        if (table.Metatable != null)
        {
            error =
                "storage values may contain only plain tables without metatables";
            return false;
        }

        if (!activeTables.Add(item: table))
        {
            error =
                "storage values may not contain cyclic table references";
            return false;
        }

        try
        {
            var entries = table.ToArray();

            if (entries.Length == 0)
            {
                node = new JsonObject();
                return true;
            }

            var allStringKeys = entries.All(
                predicate: item => item.Key.TryRead<string>(result: out _));

            var allArrayKeys = entries.All(
                predicate: item => TryReadPositiveArrayIndex(
                    value: item.Key,
                    index: out _));

            if (!allStringKeys && !allArrayKeys)
            {
                error =
                    "storage tables must use either string keys or a contiguous 1-based integer sequence";
                return false;
            }

            if (allStringKeys)
            {
                var jsonObject = new JsonObject();

                foreach (var item in entries)
                {
                    var objectKey = item.Key.Read<string>();

                    if (objectKey.Length >
                        MaximumStorageObjectKeyLength)
                    {
                        error =
                            $"storage object keys may not exceed {MaximumStorageObjectKeyLength} characters";
                        return false;
                    }

                    if (!this.TryCreateStorageNodeCore(
                            value: item.Value,
                            depth: depth + 1,
                            activeTables: activeTables,
                            nodeCount: ref nodeCount,
                            node: out var child,
                            error: out error))
                    {
                        return false;
                    }

                    jsonObject[propertyName: objectKey] = child;
                }

                node = jsonObject;
                return true;
            }

            var values = new JsonNode?[entries.Length];
            var assigned = new bool[entries.Length];

            foreach (var item in entries)
            {
                _ = TryReadPositiveArrayIndex(
                    value: item.Key,
                    index: out var index);

                if (index > entries.Length ||
                    assigned[index - 1])
                {
                    error =
                        "storage arrays must be contiguous and use each 1-based index exactly once";
                    return false;
                }

                if (!this.TryCreateStorageNodeCore(
                        value: item.Value,
                        depth: depth + 1,
                        activeTables: activeTables,
                        nodeCount: ref nodeCount,
                        node: out var child,
                        error: out error))
                {
                    return false;
                }

                assigned[index - 1] = true;
                values[index - 1] = child;
            }

            if (assigned.Any(predicate: valueAssigned => !valueAssigned))
            {
                error =
                    "storage arrays may not contain gaps";
                return false;
            }

            var jsonArray = new JsonArray();

            foreach (var child in values)
            {
                jsonArray.Add(item: child);
            }

            node = jsonArray;
            return true;
        }
        finally
        {
            activeTables.Remove(item: table);
        }
    }

    private static bool TryReadPositiveArrayIndex(
        LuaValue value,
        out int index)
    {
        index = 0;

        if (!value.TryRead<double>(result: out var number) ||
            !double.IsFinite(d: number) ||
            number < 1 ||
            number > int.MaxValue ||
            number != Math.Truncate(d: number))
        {
            return false;
        }

        index = (int)number;
        return true;
    }

    private LuaValue CreateLuaValueFromStorageNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            var table = new LuaTable(
                arrayCapacity: 0,
                dictionaryCapacity: jsonObject.Count);

            foreach (var item in jsonObject)
            {
                table[key: item.Key] = item.Value == null
                    ? LuaValue.Nil
                    : this.CreateLuaValueFromStorageNode(
                        node: item.Value);
            }

            return table;
        }

        if (node is JsonArray jsonArray)
        {
            var table = new LuaTable(
                arrayCapacity: jsonArray.Count,
                dictionaryCapacity: 0);

            for (var index = 0;
                 index < jsonArray.Count;
                 index++)
            {
                var item = jsonArray[index: index];

                table[key: index + 1] = item == null
                    ? LuaValue.Nil
                    : this.CreateLuaValueFromStorageNode(
                        node: item);
            }

            return table;
        }

        if (node is not JsonValue jsonValue)
        {
            throw new InvalidOperationException(
                "Persistent storage contained an unsupported JSON value.");
        }

        var valueKind = jsonValue.GetValueKind();

        return valueKind switch
        {
            JsonValueKind.True =>
                true,

            JsonValueKind.False =>
                false,

            JsonValueKind.String =>
                jsonValue.GetValue<string>(),

            JsonValueKind.Number =>
                jsonValue.GetValue<double>(),

            JsonValueKind.Null or
                JsonValueKind.Undefined =>
                LuaValue.Nil,

            JsonValueKind.Object or
                JsonValueKind.Array =>
                throw new JsonException(
                    $"Unexpected {valueKind} value inside a JsonValue."),

            _ =>
                throw new JsonException(
                    $"Unsupported JSON value kind: {valueKind}."),
        };

    }

    private LuaValue ToLuaValue(object? value)
    {
        if (value == null)
        {
            return LuaValue.Nil;
        }

        switch (value)
        {
            case LuaValue luaValue:
                return luaValue;
            case string text:
                return text;
            case bool boolean:
                return boolean;
            case byte number:
                return number;
            case sbyte number:
                return number;
            case short number:
                return number;
            case ushort number:
                return number;
            case int number:
                return number;
            case uint number:
                return number;
            case long number:
                return number;
            case ulong number:
                return number;
            case float number:
                return (double)number;
            case double number:
                return number;
            case decimal number:
                return (double)number;
            case DateTimeOffset timestamp:
                return timestamp.ToUnixTimeMilliseconds();
            case IReadOnlyDictionary<string, object?> dictionary:
                return this.CreateReadOnlyTable(
                    backing: this.CreateLuaTable(dictionary: dictionary));
            case IDictionary dictionary:
                return this.CreateReadOnlyTable(
                    backing: this.CreateLuaTable(dictionary: dictionary));
            case IEnumerable enumerable:
                return this.CreateReadOnlyTable(
                    backing: this.CreateLuaArray(enumerable: enumerable));
            default:
                var formattedValue = value.ToString();
                return formattedValue ?? LuaValue.Nil;
        }
    }

    private LuaTable CreateLuaTable(
        IReadOnlyDictionary<string, object?> dictionary)
    {
        var table = new LuaTable();

        foreach (var item in dictionary)
        {
            table[key: item.Key] = this.ToLuaValue(value: item.Value);
        }

        return table;
    }

    private LuaTable CreateLuaTable(IDictionary dictionary)
    {
        var table = new LuaTable();

        foreach (DictionaryEntry item in dictionary)
        {
            if (item.Key is string key)
            {
                table[key: key] = this.ToLuaValue(value: item.Value);
            }
        }

        return table;
    }

    private LuaTable CreateLuaArray(IEnumerable enumerable)
    {
        var table = new LuaTable();
        var index = 1;

        foreach (var item in enumerable)
        {
            table[key: index++] = this.ToLuaValue(value: item);
        }

        return table;
    }

    private void WriteLog(
        AddonLogLevel level,
        string message)
    {
        this.LogEntryWritten?.Invoke(
            sender: this,
            e: new AddonLogEntryEventArgs(
                new AddonLogEntry(
                    ObservedAt: DateTimeOffset.UtcNow,
                    OwnerProcessId: this.options.OwnerProcessId,
                    AddonId: this.options.Manifest.Id,
                    Level: level,
                    Message: message)));
    }

    private static LuaValue ToLuaValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value: value)
            ? LuaValue.Nil
            : value;
    }

    private static string FormatValue(LuaValue value)
    {
        if (value.Type == LuaValueType.Nil)
        {
            return "nil";
        }

        if (value.TryRead<string>(result: out var text))
        {
            return text;
        }

        if (value.TryRead<bool>(result: out var boolean))
        {
            return boolean
                ? "true"
                : "false";
        }

        if (value.TryRead<double>(result: out var number))
        {
            return number.ToString(
                format: "G17",
                provider: CultureInfo.InvariantCulture);
        }

        return value.ToString();
    }
}
