namespace HKFeedbackV2
{
    public interface IResolver<T>
    {
        T Resolve(IContainer container);
    }
}
