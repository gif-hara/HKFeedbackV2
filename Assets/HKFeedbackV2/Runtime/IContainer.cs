namespace HKFeedbackV2
{
    public interface IContainer
    {
        void Register(ContainerKey key, object obj);

        void Register<T>(ContainerKey key, T obj);

        T Resolve<T>(ContainerKey key);

        bool TryResolve<T>(ContainerKey key, out T value);
    }
}
