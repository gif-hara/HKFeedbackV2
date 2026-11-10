using UnityEditor;
using UnityEngine;

namespace HKFeedbackV2.Editor
{
    internal sealed class FeedbackBlockDraft : ScriptableObject
    {
        [SerializeReference] private IAction value;

        internal IAction Value => value;

        internal static FeedbackBlockDraft Create(IAction value)
        {
            var draft = CreateInstance<FeedbackBlockDraft>();
            draft.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            draft.value = value;
            return draft;
        }

        internal IAction CopyValue()
        {
            var copy = Instantiate(this);
            var result = copy.value;
            DestroyImmediate(copy);
            return result;
        }

        internal SerializedProperty GetValueProperty(SerializedObject serializedDraft) =>
            serializedDraft.FindProperty(nameof(value));
    }
}
