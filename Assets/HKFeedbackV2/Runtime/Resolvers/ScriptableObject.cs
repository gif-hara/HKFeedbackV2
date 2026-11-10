using System;
using HKFeedbackV2.Actions;

namespace HKFeedbackV2.Resolvers
{
    [Serializable]
    public sealed class ScriptableObject : IResolver<IAction[]>
    {
        [UnityEngine.SerializeField]
        private ScriptableAction scriptableAction = null!;

        public IAction[] Resolve(IContainer container)
        {
            return scriptableAction.Actions;
        }

        public ScriptableObject(ScriptableAction scriptableAction)
        {
            this.scriptableAction = scriptableAction;
        }

        public ScriptableObject()
        {
            this.scriptableAction = null!;
        }
    }
}
