using System;
using UnityEngine;

namespace HKFeedbackV2.Evaluators
{
    [Serializable]
    public sealed class Constant : IEvaluator
    {
        [SerializeField]
        private bool isTrue = true;

        public bool Evaluate(IContainer container) => isTrue;

        public Constant()
        {
        }

        public Constant(bool isTrue)
        {
            this.isTrue = isTrue;
        }
    }
}
