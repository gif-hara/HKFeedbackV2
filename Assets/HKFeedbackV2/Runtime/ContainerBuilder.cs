namespace HKFeedbackV2
{
    public sealed class ContainerBuilder
    {
        private readonly Container container = new();

        public static ContainerBuilder Create()
        {
            return new ContainerBuilder();
        }

        public ContainerBuilder Register<T>(T obj)
        {
            ((IContainerRegister)container).Register(obj);
            return this;
        }

        public ContainerBuilder Register<T>(ContainerKey key, T obj)
        {
            ((IContainerRegister)container).Register(key, obj);
            return this;
        }

        public IContainer Build()
        {
            return container;
        }
    }
}
