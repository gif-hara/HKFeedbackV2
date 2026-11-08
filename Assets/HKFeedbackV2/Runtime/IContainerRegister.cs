namespace HKFeedbackV2
{
    public interface IContainerRegister
    {
        void Register(ContainerKey key, IContainerBuilder builder);
    }
}
