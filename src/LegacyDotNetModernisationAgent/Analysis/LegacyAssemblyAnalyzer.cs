using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using LegacyDotNetModernisationAgent.Models;

namespace LegacyDotNetModernisationAgent.Analysis;

/// <summary>
/// Reads facts straight from a supplied binary's metadata. The assembly is never loaded or executed,
/// so a CLR 2.0 binary can be inspected from modern .NET and nothing is assumed about its version.
/// </summary>
public sealed class LegacyAssemblyAnalyzer
{
    public IReadOnlyList<LegacyAssemblyEvidence> Analyze(
        string repositoryRoot,
        IReadOnlyList<DotNetProjectAssessment> projects,
        IReadOnlySet<string> usedTypeNames)
    {
        var binaries = projects
            .SelectMany(project => project.AssemblyReferences
                .Where(reference => reference is { Kind: ReferenceKind.LocalBinary, BinaryExists: true })
                .Select(reference => (Project: project.Name, reference.HintPath)))
            .GroupBy(item => item.HintPath!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        return binaries
            .Select(group => Describe(
                repositoryRoot,
                group.Key,
                group.Select(item => item.Project).Distinct().Order(StringComparer.Ordinal).ToList(),
                usedTypeNames))
            .ToList();
    }

    private static LegacyAssemblyEvidence Describe(
        string repositoryRoot,
        string relativePath,
        IReadOnlyList<string> referencedBy,
        IReadOnlySet<string> usedTypeNames)
    {
        var fullPath = Path.Combine(repositoryRoot, relativePath);
        var bytes = File.ReadAllBytes(fullPath);

        using var peReader = new PEReader(ImmutableCollectionsMarshalBytes(bytes));
        var reader = peReader.GetMetadataReader();
        var assembly = reader.GetAssemblyDefinition();

        var publicTypes = reader.TypeDefinitions
            .Select(handle => (Handle: handle, Definition: reader.GetTypeDefinition(handle)))
            .Where(type => IsPublic(type.Definition))
            .Select(type => (type.Handle, type.Definition, FullName: FullName(reader, type.Definition)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        // When several public types share a simple name (Csla.DataPortal vs Csla.Server.DataPortal),
        // describe the one in the shallowest namespace: source that says "DataPortal" means that one.
        var used = publicTypes
            .Where(type => usedTypeNames.Contains(StripArity(reader.GetString(type.Definition.Name))))
            .GroupBy(type => reader.GetString(type.Definition.Name), StringComparer.Ordinal)
            .Select(group => group.OrderBy(type => type.FullName.Count(character => character == '.')).ThenBy(type => type.FullName, StringComparer.Ordinal).First())
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => DescribeType(reader, type.Definition, type.FullName))
            .ToList();

        return new LegacyAssemblyEvidence(
            relativePath,
            reader.GetString(assembly.Name),
            assembly.Version.ToString(),
            reader.MetadataVersion,
            PublicKeyToken(reader, assembly.PublicKey),
            Convert.ToHexString(SHA256.HashData(bytes)),
            reader.AssemblyReferences
                .Select(handle => reader.GetAssemblyReference(handle))
                .Select(reference => new AssemblyReferenceEvidence(reader.GetString(reference.Name), reference.Version.ToString()))
                .OrderBy(reference => reference.Name, StringComparer.Ordinal)
                .ToList(),
            publicTypes.Count,
            publicTypes.Select(type => type.FullName).ToList(),
            used,
            referencedBy);
    }

    private static ImmutableArray<byte> ImmutableCollectionsMarshalBytes(byte[] bytes) => [.. bytes];

    private static bool IsPublic(TypeDefinition type)
    {
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;

        return visibility == TypeAttributes.Public;
    }

    private static string StripArity(string name)
    {
        var tick = name.IndexOf('`');

        return tick < 0 ? name : name[..tick];
    }

    private static string FullName(MetadataReader reader, TypeDefinition type)
    {
        var ns = reader.GetString(type.Namespace);
        var name = reader.GetString(type.Name);

        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    private static string? PublicKeyToken(MetadataReader reader, BlobHandle publicKey)
    {
        if (publicKey.IsNil)
        {
            return null;
        }

        var key = reader.GetBlobBytes(publicKey);

        if (key.Length == 0)
        {
            return null;
        }

        // Token = last 8 bytes of SHA-1(public key), reversed.
        var hash = SHA1.HashData(key);

        return Convert.ToHexString(hash.AsSpan(hash.Length - 8).ToArray().Reverse().ToArray()).ToLowerInvariant();
    }

    private static AssemblyTypeDescription DescribeType(MetadataReader reader, TypeDefinition type, string fullName)
    {
        var typeParameters = type.GetGenericParameters()
            .Select(handle => reader.GetString(reader.GetGenericParameter(handle).Name))
            .ToList();

        var methods = new List<AssemblyMemberSignature>();

        foreach (var handle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            var access = method.Attributes & MethodAttributes.MemberAccessMask;

            var accessibility = access switch
            {
                MethodAttributes.Public => "public",
                MethodAttributes.Family => "protected",
                MethodAttributes.FamORAssem => "protected internal",
                _ => null
            };

            if (accessibility is null)
            {
                continue;
            }

            var methodParameters = method.GetGenericParameters()
                .Select(parameter => reader.GetString(reader.GetGenericParameter(parameter).Name))
                .ToList();

            var signature = method.DecodeSignature(new SignatureTextProvider(), new GenericContext(typeParameters, methodParameters));
            var name = reader.GetString(method.Name);
            var genericSuffix = methodParameters.Count == 0 ? "" : $"<{string.Join(", ", methodParameters)}>";
            var modifiers = (method.Attributes & MethodAttributes.Static) != 0 ? "static " : "";

            methods.Add(new AssemblyMemberSignature(
                accessibility,
                name,
                $"{modifiers}{signature.ReturnType} {name}{genericSuffix}({string.Join(", ", signature.ParameterTypes)})"));
        }

        string? baseType = null;

        if (!type.BaseType.IsNil)
        {
            baseType = type.BaseType.Kind switch
            {
                HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)type.BaseType)),
                HandleKind.TypeReference => RefName(reader, (TypeReferenceHandle)type.BaseType),
                HandleKind.TypeSpecification => new SignatureTextProvider()
                    .GetTypeFromSpecification(reader, new GenericContext(typeParameters, []), (TypeSpecificationHandle)type.BaseType, 0),
                _ => null
            };
        }

        var displayName = typeParameters.Count == 0 ? fullName : $"{StripArity(fullName)}<{string.Join(", ", typeParameters)}>";

        return new AssemblyTypeDescription(
            displayName,
            baseType,
            methods.OrderBy(method => method.Name, StringComparer.Ordinal).ThenBy(method => method.Signature, StringComparer.Ordinal).ToList());
    }

    private static string RefName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var ns = reader.GetString(reference.Namespace);
        var name = reader.GetString(reference.Name);

        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    private sealed record GenericContext(IReadOnlyList<string> TypeParameters, IReadOnlyList<string> MethodParameters);

    private sealed class SignatureTextProvider : ISignatureTypeProvider<string, GenericContext>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.IntPtr => "nint",
            PrimitiveTypeCode.UIntPtr => "nuint",
            _ => typeCode.ToString()
        };

        public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            FullName(metadataReader, metadataReader.GetTypeDefinition(handle));

        public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind) =>
            RefName(metadataReader, handle);

        public string GetTypeFromSpecification(MetadataReader metadataReader, GenericContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            $"{StripArity(genericType)}<{string.Join(", ", typeArguments)}>";

        public string GetGenericTypeParameter(GenericContext genericContext, int index) =>
            index < genericContext.TypeParameters.Count ? genericContext.TypeParameters[index] : $"!{index}";

        public string GetGenericMethodParameter(GenericContext genericContext, int index) =>
            index < genericContext.MethodParameters.Count ? genericContext.MethodParameters[index] : $"!!{index}";

        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string elementType) => "ref " + elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    }
}
