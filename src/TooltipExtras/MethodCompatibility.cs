using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace TooltipExtras;

// Compare only the audited managed operation, resolving tokens to names so unrelated
// assembly recompiles, version stamps and metadata token renumbering do not disable it.
internal static class MethodCompatibility
{
    private static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes).GetFields()
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => unchecked((ushort)o.Value));

    internal static string Fingerprint(MethodBase method)
    {
        var body = method.GetMethodBody() ?? throw new NotSupportedException("Managed body unavailable");
        var bytes = body.GetILAsByteArray()!;
        var instructions = new List<(int Offset, string Opcode, string Operand, int[]? Targets)>();
        var typeArgs = method.DeclaringType?.GetGenericArguments();
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (var p = 0; p < bytes.Length;)
        {
            var start = p;
            ushort value = bytes[p++]; if (value == 0xFE) value = (ushort)(0xFE00 | bytes[p++]);
            var op = Opcodes[value];
            string operand = ""; int[]? targets = null;
            int ReadInt() { var result = BitConverter.ToInt32(bytes, p); p += 4; return result; }
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget: var delta = (sbyte)bytes[p++]; targets = [p + delta]; break;
                case OperandType.InlineBrTarget: var jump = ReadInt(); targets = [p + jump]; break;
                case OperandType.InlineSwitch:
                    var count = ReadInt(); var end = checked(p + count * 4); targets = new int[count];
                    for (var i = 0; i < count; i++) targets[i] = checked(end + ReadInt());
                    break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                    operand = Member(method.Module.ResolveMember(ReadInt(), typeArgs, methodArgs)!); break;
                case OperandType.InlineString: operand = Convert.ToHexString(Encoding.UTF8.GetBytes(method.Module.ResolveString(ReadInt()))); break;
                case OperandType.InlineSig: operand = StandaloneSignature(method.Module, method.Module.ResolveSignature(ReadInt()), typeArgs, methodArgs); break;
                case OperandType.ShortInlineI: operand = ((sbyte)bytes[p++]).ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case OperandType.ShortInlineVar: operand = bytes[p++].ToString(); break;
                case OperandType.InlineVar: operand = BitConverter.ToUInt16(bytes, p).ToString(); p += 2; break;
                case OperandType.InlineI: operand = ReadInt().ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case OperandType.InlineI8: operand = Convert.ToHexString(bytes.AsSpan(p, 8)); p += 8; break;
                case OperandType.ShortInlineR: operand = Convert.ToHexString(bytes.AsSpan(p, 4)); p += 4; break;
                case OperandType.InlineR: operand = Convert.ToHexString(bytes.AsSpan(p, 8)); p += 8; break;
                default: throw new NotSupportedException("Unknown IL operand");
            }
            instructions.Add((start, op.Name!, operand, targets));
        }
        var offsets = instructions.Select((i, index) => (i.Offset, index)).ToDictionary(p => p.Offset, p => p.index);
        offsets[bytes.Length] = instructions.Count;
        var text = new StringBuilder(Member(method)).Append('|').Append(body.InitLocals);
        foreach (var local in body.LocalVariables) text.Append('|').Append(TypeName(local.LocalType)).Append(':').Append(local.IsPinned);
        foreach (var i in instructions)
        {
            var opcode = i.Targets != null && i.Opcode.EndsWith(".s", StringComparison.Ordinal) ? i.Opcode[..^2] : i.Opcode;
            text.Append('|').Append(opcode).Append(':').Append(i.Operand);
            if (i.Targets != null) foreach (var target in i.Targets) text.Append(':').Append(offsets[target]);
        }
        foreach (var region in body.ExceptionHandlingClauses)
        {
            text.Append('|').Append(region.Flags).Append(':').Append(offsets[region.TryOffset]).Append(':').Append(offsets[region.TryOffset + region.TryLength])
                .Append(':').Append(offsets[region.HandlerOffset]).Append(':').Append(offsets[region.HandlerOffset + region.HandlerLength]);
            if (region.Flags == ExceptionHandlingClauseOptions.Clause) text.Append(':').Append(TypeName(region.CatchType!));
            if (region.Flags == ExceptionHandlingClauseOptions.Filter) text.Append(':').Append(offsets[region.FilterOffset]);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static unsafe string StandaloneSignature(Module module, byte[] signature, Type[]? typeArgs, Type[]? methodArgs)
    {
        fixed (byte* ptr = signature)
        {
            var reader = new System.Reflection.Metadata.BlobReader(ptr, signature.Length);
            var decoder = new System.Reflection.Metadata.Ecma335.SignatureDecoder<string, object?>(new SignatureNames(module, typeArgs, methodArgs), null!, null);
            return SignatureNames.Format(decoder.DecodeMethodSignature(ref reader));
        }
    }
    private sealed class SignatureNames(Module module, Type[]? typeArgs, Type[]? methodArgs) : System.Reflection.Metadata.ISignatureTypeProvider<string, object?>
    {
        internal static string Format(System.Reflection.Metadata.MethodSignature<string> signature) => signature.Header.RawValue + ":" + signature.GenericParameterCount + ":" + signature.RequiredParameterCount + "(" + string.Join(",",signature.ParameterTypes) + "):" + signature.ReturnType;
        public string GetArrayType(string element, System.Reflection.Metadata.ArrayShape shape) => element + "[" + shape.Rank + ";" + string.Join(",",shape.Sizes) + ";" + string.Join(",",shape.LowerBounds) + "]";
        public string GetByReferenceType(string element) => element + "&";
        public string GetFunctionPointerType(System.Reflection.Metadata.MethodSignature<string> signature) => "fn:" + Format(signature);
        public string GetGenericInstantiation(string generic, System.Collections.Immutable.ImmutableArray<string> arguments) => generic + "<" + string.Join(",",arguments) + ">";
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetModifiedType(string modifier, string type, bool required) => type + (required ? " modreq(" : " modopt(") + modifier + ")";
        public string GetPinnedType(string type) => type + " pinned";
        public string GetPointerType(string element) => element + "*";
        public string GetPrimitiveType(System.Reflection.Metadata.PrimitiveTypeCode code) => "primitive:" + code;
        public string GetSZArrayType(string element) => element + "[]";
        public string GetTypeFromDefinition(System.Reflection.Metadata.MetadataReader reader, System.Reflection.Metadata.TypeDefinitionHandle handle, byte kind) => Resolve(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle));
        public string GetTypeFromReference(System.Reflection.Metadata.MetadataReader reader, System.Reflection.Metadata.TypeReferenceHandle handle, byte kind) => Resolve(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle));
        public string GetTypeFromSpecification(System.Reflection.Metadata.MetadataReader reader, object? context, System.Reflection.Metadata.TypeSpecificationHandle handle, byte kind) => Resolve(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(handle));
        private string Resolve(int token) => TypeName(module.ResolveType(token, typeArgs, methodArgs));
    }
    internal static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        var typeArgs = method.DeclaringType?.GetGenericArguments();
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (var p = 0; p < bytes.Length;)
        {
            ushort value = bytes[p++]; if (value == 0xFE) value = (ushort)(0xFE00 | bytes[p++]);
            var op = Opcodes[value];
            if (op.OperandType == OperandType.InlineMethod)
            {
                if (method.Module.ResolveMethod(BitConverter.ToInt32(bytes, p), typeArgs, methodArgs) is { } called) yield return called;
            }
            p += op.OperandType switch {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + 4 * BitConverter.ToInt32(bytes, p)),
                _ => 4
            };
        }
    }
    internal static IEnumerable<FieldInfo> Fields(MethodBase method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        var typeArgs = method.DeclaringType?.GetGenericArguments();
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (var p = 0; p < bytes.Length;)
        {
            ushort value = bytes[p++]; if (value == 0xFE) value = (ushort)(0xFE00 | bytes[p++]);
            var op = Opcodes[value];
            if (op.OperandType == OperandType.InlineField)
            {
                if (method.Module.ResolveField(BitConverter.ToInt32(bytes, p), typeArgs, methodArgs) is { } called) yield return called;
            }
            p += op.OperandType switch {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + 4 * BitConverter.ToInt32(bytes, p)),
                _ => 4
            };
        }
    }
    internal static string CallTree(MethodBase root)
    {
        var pending = new Stack<MethodBase>(); pending.Push(root);
        var visited = new HashSet<MethodBase>();
        while (pending.TryPop(out var method))
        {
            if (!visited.Add(method)) continue;
            if (visited.Count > 256) throw new NotSupportedException("互換性検査の呼出先数が上限を超えました");
                        foreach (var stateMachine in method.GetCustomAttributes<System.Runtime.CompilerServices.StateMachineAttribute>())
                foreach (var stateMethod in stateMachine.StateMachineType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (stateMethod.GetMethodBody() != null) pending.Push(stateMethod);
            foreach (var called in Calls(method))
                if (called.DeclaringType?.Assembly == root.DeclaringType?.Assembly && called.GetMethodBody() != null) pending.Push(called);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(";", visited.OrderBy(Member, StringComparer.Ordinal).Select(m => Member(m) + ":" + Fingerprint(m))))));
    }
    internal static string Member(MemberInfo member) => member switch
    {
        Type type => TypeName(type),
        FieldInfo field => TypeName(field.DeclaringType!) + "." + field.Name + ":" + TypeName(field.FieldType),
        MethodBase method => (method.IsStatic ? "static " : "instance ") + TypeName(method.DeclaringType!) + "." + method.Name
            + (method.IsGenericMethod ? "<" + string.Join(",", method.GetGenericArguments().Select(TypeName)) + ">" : "")
            + "(" + string.Join(",", method.GetParameters().Select(p => TypeName(p.ParameterType))) + ")"
            + (method is MethodInfo info ? ":" + TypeName(info.ReturnType) : ""),
        _ => throw new NotSupportedException("Unsupported IL member"),
    };
    private static string TypeName(Type type)
    {
        if (type.IsByRef) return TypeName(type.GetElementType()!) + "&";
        if (type.IsPointer) return TypeName(type.GetElementType()!) + "*";
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsFunctionPointer) return "fn[" + type.IsUnmanagedFunctionPointer + ";"
            + string.Join(",", type.GetFunctionPointerCallingConventions().Select(TypeName)) + "](" 
            + string.Join(",", type.GetFunctionPointerParameterTypes().Select(TypeName)) + "):" + TypeName(type.GetFunctionPointerReturnType());
        if (type.IsGenericParameter) return (type.DeclaringMethod == null ? "!" : "!!") + type.GenericParameterPosition;
        if (type.IsGenericType) return type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
        return type.FullName ?? throw new NotSupportedException("Unnamed IL type");
    }
}
