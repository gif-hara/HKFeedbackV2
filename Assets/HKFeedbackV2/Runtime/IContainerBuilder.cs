namespace HKFeedbackV2
{
    public interface IContainerBuilder
    {
        void Register<T>(ContainerKey key, T obj);
    }
}
