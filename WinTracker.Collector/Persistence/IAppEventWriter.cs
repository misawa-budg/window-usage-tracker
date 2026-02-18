internal interface IAppEventWriter : IDisposable
{
    void Write(AppEvent appEvent);
}
