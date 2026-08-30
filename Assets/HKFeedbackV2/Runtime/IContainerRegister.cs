namespace HKFeedbackV2
{
    public interface IContainerRegister
    {
        IContainerRegister Register<T>(T obj);

        IContainerRegister Register<T>(ContainerKey key, T obj);
    }
}
