using System.Reflection;
using System.Text.Json;


while (true)
{
    var request = Console.ReadLine();
    if (request == null) break;

    try
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var deserializedRequest = JsonSerializer.Deserialize<Request>(request, options);
        if (deserializedRequest == null)
        {
            Console.WriteLine("Deserialization failed.");
            continue;
        }
        var method = deserializedRequest.Method;
        Dictionary<string, object> handler = method switch
        {
            "initialize" => ReturnInitializeResponse(),
            "tools/list" => GetToolsDictionaryFromType(typeof(MyTools)),
            "tools/call" => HandleToolCall(deserializedRequest),
            _ => new Dictionary<string, object>(),
        };
        var output = FinalResponse(handler, deserializedRequest);
        Console.WriteLine(output);

    }
    catch (Exception e)
    {
        Console.WriteLine(e);
        throw;
    }
}

Dictionary<string, object> HandleToolCall(Request deserializedRequest)
{
    try
    {
        var toolsInstance = new MyTools();
        var result = ToolInvoker.InvokeTool(toolsInstance, deserializedRequest);
        return new Dictionary<string, object>
        {
            ["content"] = result
        };
    }
    catch (Exception e)
    {
        Console.WriteLine(e);
        throw;
    }
}

static Dictionary<string, object> GetToolsDictionaryFromType(Type toolsClassType)
{
    var toolsList = ToolReflectionHelper.GenerateToolsFromType(toolsClassType);
    return new Dictionary<string, object>
    {
        ["tools"] = toolsList
    };
}

Dictionary<string, object> ReturnInitializeResponse()
{
    var result = new Dictionary<string, object>
    {
        ["protocolVersion"] = "2024-11-05",
        ["capabilities"] = new Dictionary<string, object>
        {
            ["tools"] = new Dictionary<string, object>
            {
                ["listChanged"] = true
            }
        },
        ["serverInfo"] = new Dictionary<string, object>
        {
            ["name"] = "ExampleServer",
            ["version"] = "1.0.0"
        },
        ["instructions"] = "Helps returning quotes based on the time of the day."
    };
    return result;
}

string FinalResponse(Dictionary<string, object> body, Request deserializedRequest)
{
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    var response = new Response
    {
        Jsonrpc = "2.0",
        Id = deserializedRequest.Id,
        Result = body
    };
    return JsonSerializer.Serialize(response, options);
}

public class Request
{
    public required string Jsonrpc { get; set; }
    public int Id { get; set; }
    public required string Method { get; set; }
    public Dictionary<string, JsonElement>? Params { get; set; }
}

public class Response
{
    public required string Jsonrpc { get; set; }
    public int Id { get; set; }
    public IDictionary<string, object>? Result { get; set; }
}

public class MyTools
{
    [Tool("get_time_based_quotes_based", "Get a quote based on the hour of day")]
    public List<Dictionary<string, string>> GetTimeBasedQuote(int hour)
    {
        var quote = QuoteGenerator.GetQuoteForTime(hour);

        return ReturnResponse(quote);
    }

    [Tool("say_hello", "Say hello to someone")]
    public List<Dictionary<string, string>> SayHello(string name)
    {
        return ReturnResponse($"Hello, {name}!");
    }

    private List<Dictionary<string, string>> ReturnResponse(string text)
    {
        return new List<Dictionary<string, string>>
        {
            new Dictionary<string, string>
            {
                { "type", "text" },
                { "text", text }
            }
        };
    }
}

public class QuoteGenerator
{
    private static readonly Dictionary<string, List<string>> quotesByTime = new Dictionary<string, List<string>>
    {
        ["morning"] = new List<string>
        {
            "Every morning is a new beginning. Take a deep breath and start again.",
            "Rise up, start fresh, see the bright opportunity in each day.",
            "Morning is the dream renewed, the heart refreshed, and the spirit revived."
        },
        ["afternoon"] = new List<string>
        {
            "Keep going! Your afternoon is full of potential.",
            "The afternoon knows what the morning never suspected.",
            "Success usually comes to those who are too busy to be looking for it."
        },
        ["evening"] = new List<string>
        {
            "Evenings are proof that no matter what happens, every day can end beautifully.",
            "Relax and recharge; the best is yet to come.",
            "Evening is a time of real experimentation. You never want to look the same way."
        },
        ["night"] = new List<string>
        {
            "Let the night take away all your worries.",
            "The darkest night produces the brightest stars.",
            "Good night. May your dreams be sweet and your worries be light."
        }
    };

    private static string GetTimeOfDay(int hour)
    {
        if (hour >= 5 && hour < 12)
            return "morning";
        else if (hour >= 12 && hour < 17)
            return "afternoon";
        else if (hour >= 17 && hour < 21)
            return "evening";
        else
            return "night";
    }

    public static string GetQuoteForTime(int hour)
    {
        var timeOfDay = GetTimeOfDay(hour);

        var quotes = quotesByTime[timeOfDay];
        var random = new Random();

        int index = random.Next(quotes.Count);
        return quotes[index];
    }
}

[AttributeUsage(AttributeTargets.Method)]
public class ToolAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public ToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }
}

public class ToolMethodInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public InputSchema InputSchema { get; set; } = new();
}

public class InputSchema
{
    public string Type { get; set; } = "object";
    public Dictionary<string, PropertySchema> Properties { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

public class PropertySchema
{
    public string Type { get; set; } = "";
    public string Description { get; set; } = "";
}

public static class ToolReflectionHelper
{
    public static List<ToolMethodInfo> GenerateToolsFromType(Type type)
    {
        var tools = new List<ToolMethodInfo>();

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var attr = method.GetCustomAttribute<ToolAttribute>();
            if (attr == null) continue;

            var toolInfo = new ToolMethodInfo
            {
                Name = attr.Name,
                Description = attr.Description,
                InputSchema = new InputSchema()
            };

            foreach (var param in method.GetParameters())
            {
                string jsonType = MapTypeToJsonSchemaType(param.ParameterType);

                toolInfo.InputSchema.Properties[param.Name!] = new PropertySchema
                {
                    Type = jsonType,
                    Description = $"Parameter {param.Name}"
                };
                toolInfo.InputSchema.Required.Add(param.Name!);
            }

            tools.Add(toolInfo);
        }

        return tools;
    }

    private static string MapTypeToJsonSchemaType(Type type)
    {
        if (type == typeof(string))
            return "string";
        if (type == typeof(int) || type == typeof(long))
            return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "number";
        if (type == typeof(bool))
            return "boolean";
        return "string";
    }
}

public static class ToolInvoker
{
    public static List<Dictionary<string, string>> InvokeTool(object toolsInstance, Request request)
    {
        if (request.Params == null)
            throw new ArgumentException("Params cannot be null.");
        if (!request.Params.TryGetValue("name", out var nameObj) || nameObj.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Missing or invalid 'name' parameter.");
        var toolName = nameObj.GetString();
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty.");

        Dictionary<string, JsonElement>? arguments = null;

        if (request.Params.TryGetValue("arguments", out var argsObj))
        {
            if (argsObj is JsonElement argsElement && argsElement.ValueKind == JsonValueKind.Object)
            {
                arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsElement.GetRawText());
            }
            else
            {
                throw new ArgumentException("'arguments' parameter is not a valid JSON object.");
            }
        }

        var method = toolsInstance.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m =>
            {
                var attr = m.GetCustomAttribute<ToolAttribute>();
                return attr != null && attr.Name == toolName;
            });

        if (method == null)
            throw new InvalidOperationException($"Tool method '{toolName}' not found.");

        var parameters = method.GetParameters();
        var invokeParams = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];

            if (arguments != null && arguments.TryGetValue(param.Name!, out var jsonValue))
            {
                invokeParams[i] = jsonValue.Deserialize(param.ParameterType, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            else if (param.HasDefaultValue)
            {
                invokeParams[i] = param.DefaultValue;
            }
            else
            {
                throw new ArgumentException($"Missing required argument '{param.Name}'.");
            }
        }

        var result = method.Invoke(toolsInstance, invokeParams);
        if (result is List<Dictionary<string, string>> resultList)
        {
            return resultList;
        }
        else
        {
            throw new InvalidOperationException($"Tool method '{toolName}' did not return a valid result.");
        }
    }
}
