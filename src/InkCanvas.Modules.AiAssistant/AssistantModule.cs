using InkCanvas.Extensibility;
using InkCanvas.Host.Contracts;

namespace InkCanvas.Modules.AiAssistant;

public sealed class AssistantModule : IApplicationModule
{
    private AssistantService _service;
    private DocumentAiService _documents;

    public void Start(IModuleContext context)
    {
        _service = new AssistantService(context.GetService<IAiAssistantHost>(), context.GetService<ICanvasToolsHost>());
        context.RegisterCleanup(_service.Dispose);
        context.Export<IAiAssistantService>(_service);
        context.Export<IAssistantAgentBridge>(_service);
        _documents = new DocumentAiService(context.GetService<IAiAssistantHost>(), context.GetService<IAiDocumentHost>());
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
