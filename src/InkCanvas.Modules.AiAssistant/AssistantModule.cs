using InkCanvas.Extensibility;
using InkCanvas.Host.Contracts;
using InkCanvas.Modules.AiAssistant.Providers;

namespace InkCanvas.Modules.AiAssistant;

public sealed class AssistantModule : IApplicationModule
{
    private AssistantService _service;
    private DocumentAiService _documents;
    private AiConnectionStore _connections;

    public void Start(IModuleContext context)
    {
        string connectionDirectory = Path.Combine(context.GetService<IAiDocumentHost>().DataDirectory, "Connections");
        try { _connections = new AiConnectionStore(connectionDirectory); }
        catch (Exception) { _connections = null; }
        _service = new AssistantService(context.GetService<IAiAssistantHost>(), context.GetService<ICanvasToolsHost>(), _connections);
        context.RegisterCleanup(_service.Dispose);
        context.Export<IAiAssistantService>(_service);
        context.Export<IAssistantAgentBridge>(_service);
        context.Export<IAiConnectionSettings>(_service);
        _documents = new DocumentAiService(context.GetService<IAiAssistantHost>(), context.GetService<IAiDocumentHost>(), _connections);
        context.RegisterCleanup(_documents.Dispose);
        context.Export<IAiDocumentService>(_documents);
    }

    public void Stop()
    {
        _service?.Dispose();
        _service = null;
        _documents?.Dispose(); _documents = null;
    }
}
