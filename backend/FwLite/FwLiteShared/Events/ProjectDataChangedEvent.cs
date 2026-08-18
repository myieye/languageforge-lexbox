using LcmCrdt;

namespace FwLiteShared.Events;

public record ProjectDataChangedEvent(ProjectData ProjectData) : IFwEvent
{
    public FwEventType Type => FwEventType.ProjectDataChanged;
    public bool IsGlobal => false;
}
