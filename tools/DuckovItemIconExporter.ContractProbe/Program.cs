using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: DuckovItemIconExporter.ContractProbe <Duckov_Data/Managed>");
    return 64;
}

var managed = Path.GetFullPath(args[0]);
var itemStatsPath = Path.Combine(managed, "ItemStatsSystem.dll");
var corePath = Path.Combine(managed, "TeamSoda.Duckov.Core.dll");
var unityCorePath = Path.Combine(managed, "UnityEngine.CoreModule.dll");
var unityUiPath = Path.Combine(managed, "UnityEngine.UI.dll");
var unityUiModulePath = Path.Combine(managed, "UnityEngine.UIModule.dll");
var failures = new List<string>();
RequireFile(itemStatsPath); RequireFile(corePath); RequireFile(unityCorePath); RequireFile(unityUiPath); RequireFile(unityUiModulePath);
if (failures.Count == 0)
{
    using var items = new MetadataAssembly(itemStatsPath);
    using var core = new MetadataAssembly(corePath);
    using var unity = new MetadataAssembly(unityCorePath);
    using var unityUi = new MetadataAssembly(unityUiPath);
    using var unityUiModule = new MetadataAssembly(unityUiModulePath);
    Require(items.HasType("ItemStatsSystem", "ItemMetaData"), "ItemStatsSystem.ItemMetaData type is missing.");
    Require(items.FieldHasType("ItemStatsSystem", "ItemMetaData", "icon", "UnityEngine.Sprite"), "ItemMetaData.icon is not UnityEngine.Sprite.");
    Require(items.FieldExists("ItemStatsSystem", "ItemMetaData", "id"), "ItemMetaData.id is missing.");
    Require(items.FieldExists("ItemStatsSystem", "ItemMetaData", "quality"), "ItemMetaData.quality is missing.");
    Require(items.FieldExists("ItemStatsSystem", "ItemMetaData", "tags"), "ItemMetaData.tags is missing.");
    Require(items.FieldExists("ItemStatsSystem", "ItemMetaData", "caliber"), "ItemMetaData.caliber is missing.");
    Require(items.PropertyExists("ItemStatsSystem", "ItemMetaData", "Name"), "ItemMetaData.Name is missing.");
    Require(items.PropertyExists("ItemStatsSystem", "ItemMetaData", "DisplayNameKey"), "ItemMetaData.DisplayNameKey is missing.");
    Require(items.HasType("ItemStatsSystem", "ItemAssetsCollection"), "ItemAssetsCollection type is missing.");
    Require(items.FieldExists("ItemStatsSystem", "ItemAssetsCollection", "entries"), "ItemAssetsCollection.entries is missing.");
    Require(items.MethodExists("ItemStatsSystem", "ItemAssetsCollection", "GetMetaData", 1, true), "ItemAssetsCollection.GetMetaData(int) is missing or not static.");
    Require(items.FieldExists("ItemStatsSystem", "ItemAssetsCollection+Entry", "typeID"), "ItemAssetsCollection.Entry.typeID is missing.");
    Require(items.FieldExists("ItemStatsSystem", "ItemAssetsCollection+Entry", "metaData"), "ItemAssetsCollection.Entry.metaData is missing.");
    Require(items.PropertyHasType("ItemStatsSystem", "Item", "TypeID", "System.Int32"), "Item.TypeID is not an Int32 property.");
    Require(items.PropertyHasType("ItemStatsSystem", "Item", "Icon", "UnityEngine.Sprite"), "Item.Icon is not a Sprite property.");
    Require(core.HasType("Duckov.Modding", "ModBehaviour"), "Duckov.Modding.ModBehaviour type is missing.");
    Require(core.MethodExists("Duckov.Modding", "ModBehaviour", "OnAfterSetup", 0, false), "ModBehaviour.OnAfterSetup is missing.");
    Require(core.MethodExists("Duckov.Modding", "ModManager", "DeactivateMod", 1, false), "ModManager.DeactivateMod(ModInfo) is missing.");
    Require(core.HasType("Duckov.UI", "ItemDisplay"), "Duckov.UI.ItemDisplay type is missing.");
    Require(core.MethodCalls("Duckov.UI", "ItemDisplay", "Setup", "ItemStatsSystem.Item", "get_Icon") && core.MethodCalls("Duckov.UI", "ItemDisplay", "Setup", "UnityEngine.UI.Image", "set_sprite"), "ItemDisplay.Setup no longer assigns Item.Icon to Image.sprite.");
    Require(unity.HasType("UnityEngine", "Sprite"), "UnityEngine.Sprite type is missing.");
    Require(unity.HasType("UnityEngine", "RenderTexture"), "UnityEngine.RenderTexture type is missing.");
    Require(unityUi.HasType("UnityEngine.UI", "Image"), "UnityEngine.UI.Image type is missing.");
    Require(unityUi.HasType("UnityEngine.UI", "GraphicRaycaster"), "UnityEngine.UI.GraphicRaycaster type is missing.");
    Require(unityUiModule.HasType("UnityEngine", "Canvas"), "UnityEngine.Canvas type is missing.");
    Require(unityUiModule.HasType("UnityEngine", "CanvasRenderer"), "UnityEngine.CanvasRenderer type is missing.");
}

if (failures.Count > 0)
{
    foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
    return 1;
}
Console.WriteLine("PASS: native Duckov and Unity contracts verified against " + managed);
return 0;

void RequireFile(string path) { Require(File.Exists(path), "Required assembly is missing: " + path); }
void Require(bool condition, string message) { if (!condition) failures.Add(message); }

sealed class MetadataAssembly : IDisposable
{
    private readonly FileStream stream;
    private readonly PEReader pe;
    private readonly MetadataReader reader;
    private readonly StringTypeProvider provider;
    public MetadataAssembly(string path) { stream = File.OpenRead(path); pe = new PEReader(stream); reader = pe.GetMetadataReader(); provider = new StringTypeProvider(reader); }
    public void Dispose() { pe.Dispose(); stream.Dispose(); }
    public bool HasType(string ns, string name) => FindType(ns, name).HasValue;
    public bool FieldExists(string ns, string type, string field) => FindField(ns, type, field).HasValue;
    public bool FieldHasType(string ns, string type, string field, string expected) => FindField(ns, type, field) is FieldDefinitionHandle handle && reader.GetFieldDefinition(handle).DecodeSignature(provider, null) == expected;
    public bool PropertyExists(string ns, string type, string property) => FindProperty(ns, type, property).HasValue;
    public bool PropertyHasType(string ns, string type, string property, string expected) => FindProperty(ns, type, property) is PropertyDefinitionHandle handle && reader.GetPropertyDefinition(handle).DecodeSignature(provider, null).ReturnType == expected;
    public bool MethodExists(string ns, string type, string name, int parameterCount, bool mustBeStatic)
    {
        var handle = FindType(ns, type); if (!handle.HasValue) return false;
        foreach (var methodHandle in reader.GetTypeDefinition(handle.Value).GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) == name && method.DecodeSignature(provider, null).ParameterTypes.Length == parameterCount && (!mustBeStatic || (method.Attributes & System.Reflection.MethodAttributes.Static) != 0)) return true;
        }
        return false;
    }
    public bool MethodCalls(string ns, string type, string methodName, string targetType, string targetMethod)
    {
        var typeHandle = FindType(ns, type); if (!typeHandle.HasValue) return false;
        foreach (var methodHandle in reader.GetTypeDefinition(typeHandle.Value).GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != methodName || method.RelativeVirtualAddress == 0) continue;
            var body = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() ?? Array.Empty<byte>();
            for (var offset = 0; offset < body.Length;)
            {
                var opcode = ReadOpcode(body, ref offset);
                var operandSize = OperandSize(opcode, body, offset);
                if ((opcode == 0x28 || opcode == 0x6f) && operandSize == 4)
                {
                    var token = BitConverter.ToInt32(body, offset);
                    if (GetMethodIdentity(MetadataTokens.EntityHandle(token)) == targetType + "." + targetMethod) return true;
                }
                offset += operandSize;
            }
        }
        return false;
    }
    private string GetMethodIdentity(EntityHandle handle)
    {
        if (handle.Kind == HandleKind.MemberReference)
        {
            var reference = reader.GetMemberReference((MemberReferenceHandle)handle);
            return GetTypeName(reference.Parent) + "." + reader.GetString(reference.Name);
        }
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
            foreach (var type in reader.TypeDefinitions) if (reader.GetTypeDefinition(type).GetMethods().Contains((MethodDefinitionHandle)handle)) return GetTypeName(type) + "." + reader.GetString(method.Name);
        }
        return string.Empty;
    }
    private FieldDefinitionHandle? FindField(string ns, string type, string name) { var typeHandle = FindType(ns, type); if (!typeHandle.HasValue) return null; foreach (var handle in reader.GetTypeDefinition(typeHandle.Value).GetFields()) if (reader.GetString(reader.GetFieldDefinition(handle).Name) == name) return handle; return null; }
    private PropertyDefinitionHandle? FindProperty(string ns, string type, string name) { var typeHandle = FindType(ns, type); if (!typeHandle.HasValue) return null; foreach (var handle in reader.GetTypeDefinition(typeHandle.Value).GetProperties()) if (reader.GetString(reader.GetPropertyDefinition(handle).Name) == name) return handle; return null; }
    private TypeDefinitionHandle? FindType(string ns, string name) { foreach (var handle in reader.TypeDefinitions) if (GetTypeName(handle) == ns + "." + name) return handle; return null; }
    private string GetTypeName(EntityHandle handle) => handle.Kind switch { HandleKind.TypeDefinition => GetTypeName((TypeDefinitionHandle)handle), HandleKind.TypeReference => GetTypeName((TypeReferenceHandle)handle), _ => string.Empty };
    private string GetTypeName(TypeDefinitionHandle handle) { var type = reader.GetTypeDefinition(handle); var name = reader.GetString(type.Name); return type.GetDeclaringType().IsNil ? reader.GetString(type.Namespace) + "." + name : GetTypeName(type.GetDeclaringType()) + "+" + name; }
    private string GetTypeName(TypeReferenceHandle handle) { var type = reader.GetTypeReference(handle); return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name); }
    private static int ReadOpcode(byte[] il, ref int offset) { var first = il[offset++]; return first == 0xfe ? 0xfe00 | il[offset++] : first; }
    private static int OperandSize(int opcode, byte[] il, int offset)
    {
        return opcode switch
        {
            0x0e or 0x0f or 0x10 or 0x11 or 0x12 or 0x13 or 0x1f => 1,
            0x20 or 0x21 or 0x22 or 0x23 or 0x27 or 0x28 or 0x29 or 0x6f or 0x72 or 0x73 or 0x74 or 0x75 or 0x79 or 0x7b or 0x7c or 0x7d or 0x7e or 0x7f or 0x80 or 0x81 or 0x8c or 0x8d or 0x8f or 0xa3 or 0xa4 or 0xa5 or 0xb3 or 0xb4 or 0xb5 or 0xbd or 0xc2 or 0xc6 or 0xd0 or 0xd1 or 0xd2 or 0xd3 or 0xd4 or 0xd5 or 0xd6 => 4,
            0x38 or 0x39 or 0x3a or 0x3b or 0x3c or 0x3d or 0x3e or 0x3f or 0x40 or 0x41 or 0x42 or 0x43 or 0x44 or 0xdd => 4,
            0x2b or 0x2c or 0x2d or 0x2e or 0x2f or 0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35 or 0x36 or 0x37 or 0xde => 1,
            0x45 => 4 + BitConverter.ToInt32(il, offset) * 4,
            0xfe09 or 0xfe0b or 0xfe0c or 0xfe0d or 0xfe0e or 0xfe0f or 0xfe11 or 0xfe12 or 0xfe13 or 0xfe14 or 0xfe15 or 0xfe16 or 0xfe1c => 4,
            0xfe06 or 0xfe07 or 0xfe0a => 4,
            _ => 0
        };
    }
}

sealed class StringTypeProvider : ISignatureTypeProvider<string, object?>
{
    private readonly MetadataReader reader; public StringTypeProvider(MetadataReader reader) { this.reader = reader; }
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch { PrimitiveTypeCode.Int32 => "System.Int32", PrimitiveTypeCode.String => "System.String", PrimitiveTypeCode.Void => "System.Void", _ => "System." + typeCode };
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => TypeName(handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) { var type = reader.GetTypeReference(handle); return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name); }
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    private string TypeName(TypeDefinitionHandle handle) { var type = reader.GetTypeDefinition(handle); return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name); }
}
