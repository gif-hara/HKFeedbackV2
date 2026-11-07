using System;
using System.Collections.Generic;

namespace HKFeedbackV2
{
    public sealed class Container : IContainer
    {
        private readonly Dictionary<Type, Dictionary<ContainerKey, object>> objects = new();

        public void Register(ContainerKey key, object obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj), "Cannot register a null object.");
            }

            var type = obj.GetType();

            if (!objects.ContainsKey(type))
            {
                objects[type] = new();
            }

            objects[type][key] = obj;
        }

        public void Register<T>(ContainerKey key, T obj)
        {
            var t = typeof(T);
            if (!objects.ContainsKey(t))
            {
                objects[t] = new();
            }

            objects[t][key] = obj;
        }

        public T Resolve<T>(ContainerKey key)
        {
            if (TryResolve(key, out T value))
            {
                return value;
            }

            throw new InvalidOperationException($"No object registered for type {typeof(T)} with key {key}.");
        }

        public bool TryResolve<T>(ContainerKey key, out T value)
        {
            if (objects.TryGetValue(typeof(T), out var keyedObjects) && keyedObjects.TryGetValue(key, out var obj))
            {
                value = (T)obj;
                return true;
            }

            value = default;
            return false;
        }
    }
}
