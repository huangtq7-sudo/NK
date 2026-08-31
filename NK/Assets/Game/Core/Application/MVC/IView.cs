namespace Naraka.Core.Application.MVC
{
    public interface IView<in TState>
        where TState : IPresentationState
    {
        void Render(TState state);
    }
}
