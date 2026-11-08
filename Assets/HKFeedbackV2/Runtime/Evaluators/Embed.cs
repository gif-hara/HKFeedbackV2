using System;
using UnityEngine;

namespace HKFeedbackV2.Evaluators
{
    [Serializable]
    public sealed class Embed : IEvaluator
    {
        [SerializeField]
        private bool isTrue = true;

        public bool Evaluate(IContainer container) => isTrue;
    }
}
