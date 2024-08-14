namespace GenerateProtoUnity;

public class MessageProto
{
    
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required List<FieldProto> Fields { get; init; }
    
    public MessageProto? Parent { get; set; }
    public List<MessageProto> Childrens { get; set; } = new();
    public List<EnumProto> ChildrensEnums { get; set; } = new();
    public Dictionary<string, List<string>> OneOfFields { get; set; } = new();
}

public class FieldProto
{
    public required string Type { get; init; }
    public required string Name { get; init; } 
    public required int Index { get; init; }
    public required bool IsRepeated { get; init; }
    public bool IsOneOf { get; set; }
}



public class EnumProto
{
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required List<EnumFieldProto> Fields { get; init; }
    public MessageProto? Parent { get; set; }
    public bool IsOneOf { get; set; }
}

public class EnumFieldProto
{
    public required string Name { get; init; } 
    public required string OriginalName { get; init; }
    public required int Index { get; init; }
}
