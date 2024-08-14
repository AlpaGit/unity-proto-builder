using System.Text;
using GenerateProtoUnity;

var dumpFile = await File.ReadAllLinesAsync("dump.cs");
var messages = new Dictionary<string, MessageProto>();
var enums = new Dictionary<string, EnumProto>();

const string connectionNs = "Com.Ankama.Dofus.Server.Connection.Protocol";
const string gameNs = "Com.Ankama.Dofus.Server.Game.Protocol";

var currentNamespace = "";

for (var index = 0; index < dumpFile.Length; index++)
{
    var line = dumpFile[index];
    
    if(line.Contains(connectionNs))
        currentNamespace = connectionNs;
    
    if(line.Contains(gameNs))
        currentNamespace = gameNs;
    
    if (line.Contains(" : IMessage<"))
    {
        ParseClass(index, ParseClassBody);
    }
    if (line.Contains("public enum"))
    {
        ParseClass(index, ParseEnumBody);
    }
}


void ParseClass(int lineBegin, Action<string, int, int> callback)
{
    // we start reading from the beginning of the class we have to count the { and } to know when the class ends
    
    var classBegin = 0;
    var classEnd = 0;
    
    for (var index = lineBegin; index < dumpFile.Length; index++)
    {
        var line = dumpFile[index];
        
        if (line.Contains('{'))
        {
            classBegin++;
        }
        
        if (line.Contains('}'))
        {
            classEnd++;
        }

        if (classBegin == classEnd && classBegin != 0)
        {
            // Namespace: Com.Ankama.Dofus.Server.Game.Protocol.Common
            var subIndex = dumpFile[lineBegin - 1].Contains("Namespace") ? 1 : 2;
            var ns = dumpFile[lineBegin - subIndex].Split(' ')[^1];
            
            if(ns.Contains("Google.Protobuf"))
                return;

            if (string.IsNullOrEmpty(ns))
            {
                ns = currentNamespace;
            }
            
            callback(ns, lineBegin, index);
            break;
        }
    }
}

void ParseEnumBody(string ns, int begin, int end)
{
    if(string.IsNullOrEmpty(ns))
        return;
    
    var lines = dumpFile[begin..(end + 1)];
    var className = lines[0].Split(' ')[2];


    if(!ns.Contains("Com.Ankama.Dofus.Server.") && !className.Contains("Oneof"))
        return;
    

    var fields = new List<EnumFieldProto>();
    
    var nsAndClassName = $"{ns}.{className}";

    if(nsAndClassName.Contains("Interop.NtDll") || nsAndClassName.Contains("UnityTls"))
        return;
    
    var message = new EnumProto
    {
        Namespace = ns.ToLower(),
        Name = className,
        Fields = fields,
        IsOneOf = className.Contains("Oneof")
    };

    var fieldIndex = 0;
    
    for (var index = 0; index < lines.Length; index++)
    {
        var line = lines[index];
        if (!line.Contains(',') && !line.Contains(';'))
            continue;
        
        if(line.Contains("value__"))
            continue;

        var originalName = string.Empty;
        // get the previous line
        var previousLine = lines[index - 1];

        if (previousLine.Contains("OriginalName"))
        {
            originalName = previousLine.Split("\"")[1];
        }

        try
        {
            // check if it contains a =
            if (line.Contains('='))
            {
                fieldIndex = int.Parse(line.Replace(";", ",").Split('=')[1].Split(',')[0].Trim());
            }

            var name = string.Empty;

            if (line.Contains("public const"))
            {
                name = line.Split(' ')[3].Replace(";", "");
            }
            else if (line.EndsWith(','))
            {
                name = line.Split(';')[0].Trim();
            }

            if (string.IsNullOrEmpty(name))
                continue;

            fields.Add(new EnumFieldProto
            {
                Name = name,
                OriginalName = originalName,
                Index = fieldIndex
            });

            fieldIndex++;
        }
        catch (Exception e)
        {
            Console.WriteLine($"\x1B[91mError: {nsAndClassName} {e.Message}");
        }
    }
    
    if (enums.TryGetValue(nsAndClassName, out var msg) && msg.Fields.Count != fields.Count)
    {
        Console.WriteLine($"\x1B[91mError: {nsAndClassName} is duplicate has different field count");
        return;
    }
    
    if (!enums.TryAdd(nsAndClassName, message))
    {
        Console.WriteLine($"\x1B[91mError: {nsAndClassName} is duplicate");
    }
}

void ParseClassBody(string ns, int begin, int end)
{
    var lines = dumpFile[begin..(end + 1)];

    var className = lines[0].Split(':')[0].Trim().Split(' ')[^1];
    var fields = new List<FieldProto>();
    
    var nsAndClassName = $"{ns}.{className}";
    
    var message = new MessageProto
    {
        Namespace = ns.ToLower(),
        Name = className,
        Fields = fields
    };
    
    var fieldNumbers = new List<FieldNumberIndex>();

    foreach (var line in lines)
    {
        var lineTrimmed = line.Trim();
        // get every property that have a get and set
        if (!lineTrimmed.Contains(" = "))
            continue;
        
        var propertyData = lineTrimmed.Split(' ');
        var propertyAccess = propertyData[0];      
        var isConst = propertyData[1] == "const";
        var propertyType = propertyData[2];
        var propertyName = propertyData[3].Replace("FieldNumber", "");
            
        if(propertyAccess != "public" || !isConst)
            continue;

        try
        {
            fieldNumbers.Add(new FieldNumberIndex
            {
                Index = int.Parse(propertyData[5][..^1]), 
                FieldName = propertyName
            });
        }
        catch (Exception e)
        {
            Console.WriteLine($"\x1B[91mError: {nsAndClassName} {propertyName} {e.Message}");
        }
    }
    
    foreach (var line in lines)
    {
        var lineTrimmed = line.Trim();
        
        // get every property that have a get and set
        if (!lineTrimmed.Contains("get; set;") && !lineTrimmed.Contains("RepeatedField<"))
            continue;
        
        var propertyData = lineTrimmed.Split(' ');
        var propertyAccess = propertyData[0];
        var propertyType = propertyData[1];
        var propertyName = propertyData[2];
        var isRepeated = false;
        
        if(propertyType.Contains("RepeatedField<"))
        {
            propertyType = propertyType.Split('<')[1].Replace(">", "");
            isRepeated = true;
        }
            
        if(propertyAccess != "public")
            continue;

        if (propertyName.Contains("()"))
            continue;
        
        try
        {
            var fieldNumberCorresponding = fieldNumbers.FirstOrDefault(x => x.FieldName == propertyName)
                                           ?? fieldNumbers[fields.Count];

            fields.Add(new FieldProto
            {
                Name = propertyName, 
                Type = propertyType, 
                Index = fieldNumberCorresponding.Index,
                IsRepeated = isRepeated
            });
        }
        catch (Exception e)
        {
            Console.WriteLine($"\x1B[91mError: {nsAndClassName} {propertyName} {e.Message}");
        }
    }

    for (var index = 0; index < lines.Length; index++)
    {
        var line = lines[index];
        if (line.Contains("OneofCase"))
        {
            var name = line.Split(' ')[1];
            var enumAssociated = enums.Values.FirstOrDefault(x => x.Name == name);
            
            if (enumAssociated == null)
            {
                Console.WriteLine($"\x1B[91mError: {nsAndClassName} {name} not found");
                continue;
            }
            
            var realName = name.Split('.')[1].Replace("OneofCase", "");

            foreach (var field in enumAssociated.Fields)
            {
                if (field.Name == "None")
                    continue;
                
                var oneOfField = field.Name;
                if (message.OneOfFields.TryGetValue(realName, out var list))
                {
                    if(list.Contains(oneOfField))
                        continue;
                    
                    list.Add(oneOfField);
                }
                else
                {
                    message.OneOfFields.Add(realName, [oneOfField]);
                }
                
                var currField = fields.FirstOrDefault(x => x.Name == oneOfField);
                
                if (currField != null)
                {
                    currField.IsOneOf = true;
                }
            }
        }
    }

    
    if (messages.TryGetValue(nsAndClassName, out var msg) && msg.Fields.Count != fields.Count)
    {
        Console.WriteLine($"\x1B[91mError: {nsAndClassName} is duplicate has different field count");
        return;
    }

    if (!messages.TryAdd(nsAndClassName, message))
    {
        Console.WriteLine($"\x1B[91mError: {nsAndClassName} is duplicate");
    }
          
    Console.WriteLine($"\x1B[93mAdded: {nsAndClassName}");
}

Directory.CreateDirectory("output");


// we apply some transformation
foreach (var msg in messages.Values)
{
    var index = msg.Name.LastIndexOf("Types", StringComparison.Ordinal);

    // we set the parent if needeed
    if (index != -1)
    {
        var parentName = msg.Name[..(index - 1)];
        
        var parent = messages.Values.FirstOrDefault(x => x.Name == parentName);
        
        if (parent != null)
        {
            msg.Parent = parent;
            msg.Parent.Childrens.Add(msg);
        }
    }
}

foreach (var en in enums.Values)
{
    var index = en.Name.LastIndexOf("Types", StringComparison.Ordinal);

    // we set the parent if needeed
    if (index != -1)
    {
        var parentName = en.Name[..(index - 1)];
        
        var parent = messages.Values.FirstOrDefault(x => x.Name == parentName);
        
        if (parent != null)
        {
            en.Parent = parent;
            en.Parent.ChildrensEnums.Add(en);
        }
    }
}

var sbs = new Dictionary<string, StringBuilder>();

foreach (var en in enums.Values)
{
    // we only want to generate enums that are in the game or connection namespace
    if(!messages.Any(x => x.Value.Fields.Any(f => f.Type == en.Name)))
        continue;
 
    if(en.Parent != null)
        continue;

    var dir = "Enums";
    
    if(!sbs.TryGetValue(dir, out var sb))
    {
        sb = new StringBuilder();
        sb.AppendLine("syntax = \"proto3\";");
        sb.AppendLine();
        sb.AppendLine("package type.ankama.com;");
        sbs.Add(dir, sb);
    }
    
    sb.AppendLine();
    
    WriteEnum(sb, en);

    try
    {
        Directory.CreateDirectory($"output/Enums/{Path.GetDirectoryName(en.Name)}");
        await File.WriteAllTextAsync($"output/Enums/{en.Name}.proto", sb.ToString());
    }
    catch (Exception e)
    {
        Console.WriteLine($"\x1B[91mError: {en.Name} {e.Message}");
    }
}

foreach (var message in messages.Values)
{
    if(message.Parent != null)
        continue;
    
    var dir = "Misc";
    
    if(message.Namespace.StartsWith("com.ankama.dofus.server.connection.protocol"))
    {
        dir = "Login";
    }
    else if(message.Namespace.StartsWith("com.ankama.dofus.server.game.protocol"))
    {
        dir = "Game";
    }
    else if(message.Namespace.StartsWith("core.services.chatservice.ankamachatservice.protocol"))
    {
        dir = "Chat";
    }
    
    if(!sbs.TryGetValue(dir, out var sb))
    {
        sb = new StringBuilder();
        sb.AppendLine("syntax = \"proto3\";");
        sb.AppendLine();
        sb.AppendLine("import \"google/protobuf/any.proto\";");  
        sb.AppendLine("package type.ankama.com;");
        sbs.Add(dir, sb);
        
        foreach (var en in enums.Values)
        {
            // we only want to generate enums that are in the game or connection namespace
            if(!messages.Any(x => x.Value.Fields.Any(f => f.Type == en.Name)))
                continue;
 
            if(en.Parent != null)
                continue;
            
            sb.AppendLine();
    
            WriteEnum(sb, en);
        }
    }
    
    //sb.AppendLine($"package type.ankama.com/{message.Namespace};");
    sb.AppendLine();
    
    
    WriteMessage(sb, message);
    // we create the directory if it doesn't exist
    
    try
    {
        Directory.CreateDirectory($"output/{dir}/{message.Namespace}/{Path.GetDirectoryName(message.Name)}");
        await File.WriteAllTextAsync($"output/{dir}/{message.Namespace}/{message.Name}.proto", sb.ToString());
    }
    catch (Exception e)
    {
        Console.WriteLine($"\x1B[91mError: {message.Name} {e.Message}");
    }
}

foreach (var (key, value) in sbs)
{
    try
    {
        await File.WriteAllTextAsync($"output/{key}.proto", value.ToString());
    }
    catch (Exception e)
    {
        Console.WriteLine($"\x1B[91mError: {key} {e.Message}");
    }
}


void WriteMessage(StringBuilder stringBuilder, MessageProto messageProto, string offsetTab = "")
{
    stringBuilder.AppendLine($"{offsetTab}message {GetRealType(messageProto.Name)} {{");

    var prefix = offsetTab + "\t";
    
    foreach (var field in messageProto.Fields)
    {
        if(field.IsOneOf)
            continue;
        
        var newType = CSharpToProtoType(field.Type);
        
        if (field.IsRepeated)
        {
            newType = $"repeated {newType}";
        }
        
        stringBuilder.AppendLine($"{prefix}{newType} {field.Name} = {field.Index};");
    }
    
    foreach (var oneOf in messageProto.OneOfFields)
    {
        stringBuilder.AppendLine($"{prefix}oneof {oneOf.Key} {{");
        
        foreach (var field in oneOf.Value)
        {
            var newType = CSharpToProtoType(messageProto.Fields.First(x => x.Name == field).Type);
            stringBuilder.AppendLine($"{prefix}\t{newType} {field} = {messageProto.Fields.First(x => x.Name == field).Index};");
        }
        
        stringBuilder.AppendLine($"{prefix}}}");
    }
    
    // we have to write the childs
    foreach (var child in messageProto.Childrens)
    {
        WriteMessage(stringBuilder, child, offsetTab + "\t");
    }
    
    foreach (var child in messageProto.ChildrensEnums)
    {
        WriteEnum(stringBuilder, child, offsetTab + "\t");
    }
    
    stringBuilder.AppendLine($"{offsetTab}}}");

    string CSharpToProtoType(string type)
    {
        return type switch
        {
            "string" => "string",
            "int" => "int32",
            "uint" => "uint32",
            "short" => "int32",
            "ushort" => "uint32",
            "long" => "int64",
            "ulong" => "uint64",
            "bool" => "bool",
            "float" => "float",
            "double" => "double",
            "Any" => "google.protobuf.Any",
            _ => GetRealType(type)
        };
    }
}

void WriteEnum(StringBuilder sb1, EnumProto enumProto, string offsetTab = "")
{
    if (enumProto.IsOneOf || enumProto.Name.Contains("OneofCase"))
        return;

    sb1.AppendLine($"{offsetTab}enum {GetRealType(enumProto.Name)} {{");
    
    foreach (var field in enumProto.Fields)
    {
        sb1.AppendLine($"{offsetTab}\t{field.OriginalName} = {field.Index};");
    }
    
    sb1.AppendLine($"{offsetTab}}}");
}

string GetRealType(string type)
{
    if (type.Contains("Types."))
    {
        var index = type.LastIndexOf("Types.", StringComparison.Ordinal);
        var realType = type[(index + 6)..];
        
        return realType;
    }
    
    return type;
}