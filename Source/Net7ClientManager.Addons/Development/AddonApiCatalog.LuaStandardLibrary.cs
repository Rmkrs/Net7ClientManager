namespace Net7ClientManager.Addons.Development;

public static partial class AddonApiCatalog
{
    private static void AddLuaStandardLibrarySymbols(
        List<AddonApiSymbol> result)
    {
        AddFunction(result, "assert", "assert(value [, message])", "Raise an error when value is false or nil.", "any", Param("value", "any"), Param("message", "any|nil"));
        AddFunction(result, "error", "error(message [, level])", "Raise a Lua error.", "nil", Param("message", "any"), Param("level", "integer|nil"));
        AddFunction(result, "getmetatable", "getmetatable(object)", "Return an object's metatable when exposed by the sandbox.", "table|nil", Param("object", "any"));
        AddFunction(result, "ipairs", "ipairs(table)", "Iterate consecutive integer keys starting at one.", "fun(): integer, any", Param("table", "table"));
        AddFunction(result, "next", "next(table [, index])", "Return the next key and value in a table.", "any, any", Param("table", "table"), Param("index", "any|nil"));
        AddFunction(result, "pairs", "pairs(table)", "Iterate all keys and values in a table.", "fun(): any, any", Param("table", "table"));
        AddFunction(result, "pcall", "pcall(function, ...)", "Call a function in protected mode.", "boolean, any", Param("callback", "function"), Param("...", "any"));
        AddFunction(result, "print", "print(...)", "Write an informational entry to the Addon Center activity log.", "nil", Param("...", "any"));
        AddFunction(result, "rawequal", "rawequal(value1, value2)", "Compare two values without invoking metamethods.", "boolean", Param("value1", "any"), Param("value2", "any"));
        AddFunction(result, "rawget", "rawget(table, index)", "Read a table key without invoking metamethods.", "any", Param("table", "table"), Param("index", "any"));
        AddFunction(result, "rawlen", "rawlen(value)", "Return the raw length of a table or string.", "integer", Param("value", "table|string"));
        AddFunction(result, "require", "require(module_name)", "Load a packaged addon module from lib/. Host filesystem search is disabled.", "any", Param("module_name", "string"));
        AddFunction(result, "select", "select(index, ...)", "Return arguments after index, or the argument count for '#'.", "any", Param("index", "integer|string"), Param("...", "any"));
        AddFunction(result, "tonumber", "tonumber(value [, base])", "Convert a value to a number.", "number|nil", Param("value", "any"), Param("base", "integer|nil"));
        AddFunction(result, "tostring", "tostring(value)", "Convert a value to a string.", "string", Param("value", "any"));
        AddFunction(result, "type", "type(value)", "Return the Lua type name for a value.", "string", Param("value", "any"));
        AddFunction(result, "xpcall", "xpcall(function, handler)", "Call a function in protected mode with an error handler.", "boolean, any", Param("callback", "function"), Param("handler", "function"));

        AddTable(result, "math", "Lua 5.2 mathematical functions and constants.");
        AddField(result, "math.huge", "number", "Positive infinity.");
        AddField(result, "math.pi", "number", "The value of pi.");
        AddStandardFunctions(
            result,
            "math",
            ("abs", "math.abs(x)", "Absolute value."),
            ("acos", "math.acos(x)", "Arc cosine in radians."),
            ("asin", "math.asin(x)", "Arc sine in radians."),
            ("atan", "math.atan(x)", "Arc tangent in radians."),
            ("atan2", "math.atan2(y, x)", "Arc tangent using both arguments."),
            ("ceil", "math.ceil(x)", "Smallest integer not less than x."),
            ("cos", "math.cos(x)", "Cosine in radians."),
            ("cosh", "math.cosh(x)", "Hyperbolic cosine."),
            ("deg", "math.deg(x)", "Convert radians to degrees."),
            ("exp", "math.exp(x)", "e raised to x."),
            ("floor", "math.floor(x)", "Largest integer not greater than x."),
            ("fmod", "math.fmod(x, y)", "Remainder of x divided by y."),
            ("frexp", "math.frexp(x)", "Split x into normalized fraction and exponent."),
            ("ldexp", "math.ldexp(m, e)", "Compute m times two raised to e."),
            ("log", "math.log(x [, base])", "Logarithm of x."),
            ("max", "math.max(...)", "Maximum argument."),
            ("min", "math.min(...)", "Minimum argument."),
            ("modf", "math.modf(x)", "Integer and fractional parts of x."),
            ("pow", "math.pow(x, y)", "x raised to y."),
            ("rad", "math.rad(x)", "Convert degrees to radians."),
            ("random", "math.random([m [, n]])", "Pseudo-random number."),
            ("randomseed", "math.randomseed(x)", "Seed the pseudo-random generator."),
            ("sin", "math.sin(x)", "Sine in radians."),
            ("sinh", "math.sinh(x)", "Hyperbolic sine."),
            ("sqrt", "math.sqrt(x)", "Square root."),
            ("tan", "math.tan(x)", "Tangent in radians."),
            ("tanh", "math.tanh(x)", "Hyperbolic tangent."));

        AddTable(result, "string", "Lua 5.2 string functions.");
        AddStandardFunctions(
            result,
            "string",
            ("byte", "string.byte(value [, i [, j]])", "Numeric byte values."),
            ("char", "string.char(...)", "Build a string from byte values."),
            ("dump", "string.dump(function)", "Binary representation of a Lua function."),
            ("find", "string.find(value, pattern [, init [, plain]])", "Find a pattern or literal substring."),
            ("format", "string.format(format, ...)", "Format values into a string."),
            ("gmatch", "string.gmatch(value, pattern)", "Iterate pattern matches."),
            ("gsub", "string.gsub(value, pattern, replacement [, n])", "Replace pattern matches."),
            ("len", "string.len(value)", "String length."),
            ("lower", "string.lower(value)", "Lower-case string."),
            ("match", "string.match(value, pattern [, init])", "Capture the first pattern match."),
            ("rep", "string.rep(value, n [, separator])", "Repeat a string."),
            ("reverse", "string.reverse(value)", "Reverse a string."),
            ("sub", "string.sub(value, i [, j])", "Substring by one-based indices."),
            ("upper", "string.upper(value)", "Upper-case string."));

        AddTable(result, "table", "Lua 5.2 table functions.");
        AddStandardFunctions(
            result,
            "table",
            ("concat", "table.concat(list [, separator [, i [, j]]])", "Join list values into a string."),
            ("insert", "table.insert(list, [position,] value)", "Insert a value into a list."),
            ("pack", "table.pack(...)", "Pack arguments into a table with an n field."),
            ("remove", "table.remove(list [, position])", "Remove and return a list value."),
            ("sort", "table.sort(list [, comparator])", "Sort a list in place."),
            ("unpack", "table.unpack(list [, i [, j]])", "Return list elements as multiple values."));

        AddTable(result, "bit32", "Lua 5.2 32-bit bitwise functions.");
        AddStandardFunctions(
            result,
            "bit32",
            ("arshift", "bit32.arshift(x, displacement)", "Arithmetic right shift."),
            ("band", "bit32.band(...)", "Bitwise AND."),
            ("bnot", "bit32.bnot(x)", "Bitwise NOT."),
            ("bor", "bit32.bor(...)", "Bitwise OR."),
            ("btest", "bit32.btest(...)", "True when bitwise AND is non-zero."),
            ("bxor", "bit32.bxor(...)", "Bitwise XOR."),
            ("extract", "bit32.extract(n, field [, width])", "Extract a bit field."),
            ("lrotate", "bit32.lrotate(x, displacement)", "Rotate left."),
            ("lshift", "bit32.lshift(x, displacement)", "Logical left shift."),
            ("replace", "bit32.replace(n, value, field [, width])", "Replace a bit field."),
            ("rrotate", "bit32.rrotate(x, displacement)", "Rotate right."),
            ("rshift", "bit32.rshift(x, displacement)", "Logical right shift."));

        AddTable(result, "package", "Restricted module-loader state. Filesystem searching is disabled.");
        AddField(result, "package.loaded", "table<string, any>", "Loaded module cache.");
        AddField(result, "package.preload", "table<string, function>", "Preloaded module functions.");
    }

    private static void AddStandardFunctions(
        List<AddonApiSymbol> result,
        string parentPath,
        params (string Name, string Signature, string Description)[] functions)
    {
        foreach (var function in functions)
        {
            AddFunction(
                result,
                string.Concat(parentPath, ".", function.Name),
                function.Signature,
                function.Description,
                "any");
        }
    }
}
