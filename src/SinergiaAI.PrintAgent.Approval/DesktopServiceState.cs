using System.ServiceProcess;
namespace SinergiaAI.PrintAgent.Approval;

internal sealed record DesktopServiceState(ServiceControllerStatus? Status,bool Changing)
{
    internal bool CanStart => !Changing && Status == ServiceControllerStatus.Stopped;
    internal bool CanStop => !Changing && Status is ServiceControllerStatus.Running or ServiceControllerStatus.Paused;
    internal bool CanManageSites => !Changing && Status == ServiceControllerStatus.Running;
    internal string Caption => Status switch
    {
        ServiceControllerStatus.Running => UiText.Get("Service running","Serviço ativo"),
        ServiceControllerStatus.Stopped => UiText.Get("Service stopped","Serviço parado"),
        ServiceControllerStatus.StartPending => UiText.Get("Starting service…","Iniciando serviço…"),
        ServiceControllerStatus.StopPending => UiText.Get("Stopping service…","Parando serviço…"),
        ServiceControllerStatus.Paused => UiText.Get("Service paused","Serviço pausado"),
        ServiceControllerStatus.PausePending or ServiceControllerStatus.ContinuePending => UiText.Get("Updating service…","Atualizando serviço…"),
        _ => UiText.Get("Service status unavailable","Status do serviço indisponível")
    };
}
