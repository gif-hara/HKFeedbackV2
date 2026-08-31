using System;
using HKFeedbackV2.Resolvers;
using UnityEngine;

namespace HKFeedbackV2.Test.Resolvers
{
    [Serializable]
    public sealed class FromTestSceneControllerAnonymous : IResolver<Transform>, IResolver<GameObject>
    {
        Transform IResolver<Transform>.Resolve(IContainer container) => container.Resolve<TestSceneController>().transform;

        GameObject IResolver<GameObject>.Resolve(IContainer container) => container.Resolve<TestSceneController>().gameObject;
    }

    [Serializable]
    public sealed class FromTestSceneControllerWithKey : IResolver<Transform>, IResolver<GameObject>
    {
        [SerializeReference, SubclassSelector]
        private IResolver<ContainerKey> key = new Constant<ContainerKey>(null);

        Transform IResolver<Transform>.Resolve(IContainer container) => container.Resolve<TestSceneController>(key.Resolve(container)).transform;

        GameObject IResolver<GameObject>.Resolve(IContainer container) => container.Resolve<TestSceneController>(key.Resolve(container)).gameObject;
    }
}
