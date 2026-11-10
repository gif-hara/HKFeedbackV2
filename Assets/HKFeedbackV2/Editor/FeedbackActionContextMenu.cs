using System;
using System.Collections.Generic;
using HKFeedbackV2.Actions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HKFeedbackV2.Editor
{
    [InitializeOnLoad]
    internal static class FeedbackActionContextMenu
    {
        private const string SessionKey = "HKFeedbackV2.ActionClipboardSession";
        private static readonly string ActionFieldType = $"{typeof(IAction).Assembly.GetName().Name} {typeof(IAction).FullName}";
        private static readonly string ClipboardPrefix;

        static FeedbackActionContextMenu()
        {
            var session = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(session))
            {
                session = Guid.NewGuid().ToString("N");
                SessionState.SetString(SessionKey, session);
            }
            // JsonUtility stores Unity object references using IDs local to the running Editor.
            ClipboardPrefix = $"HKFeedbackV2.Action:{session}:";
            EditorApplication.contextualPropertyMenu += AddMenuItems;
        }

        private static bool IsActionField(SerializedProperty property) =>
            property != null && property.propertyType == SerializedPropertyType.ManagedReference &&
            property.managedReferenceFieldTypename == ActionFieldType;

        private static bool IsActionArray(SerializedProperty property)
        {
            if (property == null || !property.isArray || property.type != nameof(IAction) ||
                property.arrayElementType != "managedReference<>") return false;

            if (property.arraySize == 0) return true;
            using var item = property.GetArrayElementAtIndex(0);
            return IsActionField(item);
        }

        private static void AddMenuItems(GenericMenu menu, SerializedProperty property)
        {
            var isArray = IsActionArray(property);
            if (!isArray && !IsActionField(property)) return;

            var owner = property.serializedObject.targetObject;
            var path = property.propertyPath;
            var canEdit = !property.serializedObject.isEditingMultipleObjects && property.editable;
            var canCopy = canEdit && (isArray || property.managedReferenceValue is IAction);

            AddCommand("To ScriptableObject", canCopy, ExportAction);
            AddCommand("Copy Action", canCopy, CopyAction);
            AddCommand("Paste Action", canEdit && HasActionClipboard(EditorGUIUtility.systemCopyBuffer, isArray), PasteAction);

            void AddCommand(string label, bool enabled, Action<SerializedProperty, bool> command)
            {
                var content = new GUIContent(label);
                if (!enabled)
                {
                    menu.AddDisabledItem(content);
                    return;
                }

                menu.AddItem(content, false, () =>
                {
                    if (!owner) return;
                    using var serializedOwner = new SerializedObject(owner);
                    using var current = serializedOwner.FindProperty(path);
                    if ((isArray ? IsActionArray(current) : IsActionField(current)) && current.editable)
                        command(current, isArray);
                });
            }
        }

        private static string GetClipboardPrefix(bool isArray) =>
            isArray ? ClipboardPrefix + "Array:" : ClipboardPrefix;

        private static bool HasActionClipboard(string text, bool isArray) =>
            text.StartsWith(GetClipboardPrefix(isArray) + "{", StringComparison.Ordinal);

        private static IAction ReadAction(SerializedProperty property, bool isArray)
        {
            if (!isArray) return property.managedReferenceValue as IAction;

            var values = new IAction[property.arraySize];
            for (var i = 0; i < values.Length; i++)
            {
                using var item = property.GetArrayElementAtIndex(i);
                values[i] = item.managedReferenceValue as IAction;
            }
            return new FeedbackSequence(values);
        }

        private static void WriteActions(SerializedProperty property, IReadOnlyList<IAction> actions)
        {
            property.arraySize = actions.Count;
            for (var i = 0; i < actions.Count; i++)
            {
                using var item = property.GetArrayElementAtIndex(i);
                item.managedReferenceValue = actions[i];
            }
        }

        private static void CopyAction(SerializedProperty property, bool isArray)
        {
            var action = ReadAction(property, isArray);
            if (action == null) return;
            var draft = FeedbackBlockDraft.Create(action);
            try
            {
                EditorGUIUtility.systemCopyBuffer = GetClipboardPrefix(isArray) + JsonUtility.ToJson(draft);
            }
            finally
            {
                Object.DestroyImmediate(draft);
            }
        }

        private static void PasteAction(SerializedProperty property, bool isArray)
        {
            var clipboard = EditorGUIUtility.systemCopyBuffer;
            if (!HasActionClipboard(clipboard, isArray)) return;

            var draft = FeedbackBlockDraft.Create(null);
            try
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(clipboard.Substring(GetClipboardPrefix(isArray).Length), draft);
                }
                catch (ArgumentException)
                {
                    EditorUtility.DisplayDialog("Paste Action", "The clipboard does not contain a valid action.", "OK");
                    return;
                }

                var sequence = draft.Value as FeedbackSequence;
                if (draft.Value == null || (isArray && sequence?.Feedbacks == null) ||
                    SerializationUtility.HasManagedReferencesWithMissingTypes(draft))
                {
                    EditorUtility.DisplayDialog("Paste Action", "The action or one of its parameter types could not be loaded.", "OK");
                    return;
                }

                using var serializedDraft = new SerializedObject(draft);
                using var value = draft.GetValueProperty(serializedDraft);
                if (!CanStoreReferences(value, property.serializedObject.targetObject))
                {
                    EditorUtility.DisplayDialog("Paste Action", "This field cannot store the action's scene references.", "OK");
                    return;
                }

                if (isArray) WriteActions(property, sequence.Feedbacks);
                else property.managedReferenceValue = draft.Value;
                property.serializedObject.ApplyModifiedProperties();
            }
            finally
            {
                Object.DestroyImmediate(draft);
            }
        }

        private static void ExportAction(SerializedProperty property, bool isArray)
        {
            var action = ReadAction(property, isArray);
            if (action == null) return;
            if (!CanStoreReferences(property, null))
            {
                EditorUtility.DisplayDialog("To ScriptableObject", "A ScriptableAction asset can only store references to other assets.", "OK");
                return;
            }

            var path = EditorUtility.SaveFilePanelInProject(
                "To ScriptableObject", isArray ? property.displayName : action.GetType().Name, "asset",
                "Save the selected actions as a ScriptableAction.");
            if (string.IsNullOrEmpty(path)) return;

            var draft = FeedbackBlockDraft.Create(action);
            try
            {
                var copy = draft.CopyValue();
                var actions = isArray ? ((FeedbackSequence)copy).Feedbacks : new[] { copy };
                SaveActionAsset(actions, AssetDatabase.GenerateUniqueAssetPath(path));
            }
            finally
            {
                Object.DestroyImmediate(draft);
            }
        }

        private static void SaveActionAsset(IReadOnlyList<IAction> values, string path)
        {
            var asset = ScriptableObject.CreateInstance<ScriptableAction>();
            try
            {
                using var serializedAsset = new SerializedObject(asset);
                using var actions = serializedAsset.FindProperty("actions");
                WriteActions(actions, values);
                serializedAsset.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.SaveAssetIfDirty(asset);
                EditorGUIUtility.PingObject(asset);
            }
            finally
            {
                if (!EditorUtility.IsPersistent(asset)) Object.DestroyImmediate(asset);
            }
        }

        private static bool CanStoreReferences(SerializedProperty action, Object owner)
        {
            var assetDestination = owner == null || EditorUtility.IsPersistent(owner);
            using var item = action.Copy();
            var depth = item.depth;
            var visited = new HashSet<long>();
            if (item.propertyType == SerializedPropertyType.ManagedReference) visited.Add(item.managedReferenceId);
            var enterChildren = true;
            while (item.Next(enterChildren) && item.depth > depth)
            {
                enterChildren = item.propertyType != SerializedPropertyType.ManagedReference ||
                    visited.Add(item.managedReferenceId);
                if (item.propertyType != SerializedPropertyType.ObjectReference ||
                    item.objectReferenceValue is not Object reference || !reference ||
                    EditorUtility.IsPersistent(reference)) continue;

                if (assetDestination) return false;
                if (!EditorSceneManager.preventCrossSceneReferences || owner is not Component component) continue;
                var referencedObject = reference is Component other ? other.gameObject : reference as GameObject;
                if (referencedObject && referencedObject.scene != component.gameObject.scene) return false;
            }
            return true;
        }
    }
}
