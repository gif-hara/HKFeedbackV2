using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HKFeedbackV2.Editor
{
    internal sealed class FeedbackGraphSession
    {
        [NoAutoStaticsCleanup] private static readonly Dictionary<Hash128, FeedbackGraphSession> Sessions = new();
        [NoAutoStaticsCleanup] private static double nextWindowCheck;

        private readonly Object owner;
        private readonly string propertyPath;
        private readonly string stackTitle;
        // Use separate serialized drafts so parameter controls can edit managed references and scene objects.
        private readonly Dictionary<string, (Hash128 NodeId, FeedbackBlockDraft Draft)> drafts = new();
        private readonly List<Overlay> collapsedPanels = new();
        private (FeedbackBlockDraft Draft, int Revision)[] syncedBlocks;
        private Hash128 graphId;
        private ScriptableObject graphObject;
        private EditorWindow window;
        private uint propertyHash;
        private bool inspectorRefreshPending;

        internal Vector2 StackSize { get; private set; }
        private bool HasOpenWindow => window && window is IGraphWindow view && Find(view.Graph) == this;

        static FeedbackGraphSession()
        {
            AssemblyReloadEvents.beforeAssemblyReload += CloseAll;
            EditorApplication.quitting += CloseAll;
            Undo.undoRedoPerformed += SynchronizeAfterUndo;
        }

        private FeedbackGraphSession(SerializedProperty property)
        {
            owner = property.serializedObject.targetObject;
            propertyPath = property.propertyPath;
            propertyHash = property.contentHash;
            stackTitle = owner.GetType().Name + "." + property.name;
            if (GetStackLayoutKey() is { } layoutKey)
            {
                StackSize = new Vector2(EditorPrefs.GetFloat(layoutKey + ".Width"), EditorPrefs.GetFloat(layoutKey + ".Height"));
            }
        }

        internal static void Open(Object owner, string propertyPath)
        {
            if (!owner) return;
            var existing = Sessions.Values.FirstOrDefault(session =>
                session.owner == owner && session.propertyPath == propertyPath);
            if (existing != null)
            {
                if (existing.HasOpenWindow)
                {
                    existing.window.Focus();
                    return;
                }
                existing.Close();
            }

            using var serializedOwner = new SerializedObject(owner);
            using var property = serializedOwner.FindProperty(propertyPath);
            if (property == null) return;
            var session = new FeedbackGraphSession(property);
            try
            {
                session.graphObject = FeedbackGraphWindow.CreateGraph(session.stackTitle, out var graph);
                session.graphId = graph.ID;
                var stack = session.CreateStack(graph);
                session.PopulateBlocks(stack, ReadFeedbacks(property));
                session.syncedBlocks = CaptureBlocks(session.GetBlockDrafts(stack));
                FeedbackGraphWindow.ClearDirty(session.graphObject);

                Sessions.Add(session.graphId, session);
                session.window = FeedbackGraphWindow.Show(session.graphObject);
                if (!session.HasOpenWindow)
                {
                    throw new InvalidOperationException("Could not open the Feedback Graph in Graph Toolkit.");
                }
                session.ConfigureWindow();
                if (Sessions.Count == 1) EditorApplication.update += CloseUnusedSessions;
            }
            catch (Exception exception)
            {
                session.Close();
                UnityEngine.Debug.LogException(exception, owner);
            }
        }

        internal static FeedbackGraphSession Find(Graph graph) =>
            graph is FeedbackGraph && Sessions.TryGetValue(graph.ID, out var session) ? session : null;

        internal void SetStackSize(Vector2 size)
        {
            StackSize = size;
            if (GetStackLayoutKey() is not { } layoutKey) return;
            EditorPrefs.SetFloat(layoutKey + ".Width", size.x);
            EditorPrefs.SetFloat(layoutKey + ".Height", size.y);
        }

        private string GetStackLayoutKey()
        {
            var ownerId = GlobalObjectId.GetGlobalObjectIdSlow(owner);
            return ownerId.assetGUID != default && ownerId.targetObjectId != 0
                ? "HKFeedbackV2.StackSize." + Hash128.Compute($"{Application.dataPath}|{ownerId}|{propertyPath}")
                : null;
        }

        internal void Save(Graph graph)
        {
            if (!owner || Find(graph) != this) return;
            Synchronize(graph);

            if (EditorUtility.IsPersistent(owner))
            {
                AssetDatabase.SaveAssetIfDirty(owner);
                return;
            }

            if (owner is not Component component || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var gameObject = component.gameObject;
            if (PrefabStageUtility.GetPrefabStage(gameObject) is { } stage)
            {
                if (stage == PrefabStageUtility.GetCurrentPrefabStage() && stage.IsPartOfPrefabContents(gameObject))
                {
                    EditorApplication.ExecuteMenuItem("File/Save");
                }
                return;
            }

            var scene = gameObject.scene;
            if (scene.IsValid() && scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene) &&
                EditorSceneManager.SaveScene(scene) && StackSize != Vector2.zero)
            {
                SetStackSize(StackSize);
            }
        }

        internal void ShowActionMenu(FeedbackStackNode stack)
        {
            var menu = new GenericMenu();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IAction>()
                         .Where(IsCreatable)
                         .OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                var label = type.FullName.Replace("+Actions+", "/").Replace('+', '/');
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    if (Find(stack.Graph) != this) return;
                    var action = (IAction)Activator.CreateInstance(type, nonPublic: true);
                    AddBlock(stack, FeedbackBlockDraft.Create(action));
                });
            }
            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("No actions found"));
            menu.ShowAsContext();
        }

        private void AddBlock(FeedbackStackNode stack, FeedbackBlockDraft draft, int index = -1)
        {
            var graph = stack.Graph;
            graph.UndoBeginRecordGraph("Add Feedback Block", stack);
            try
            {
                AttachDraft(CreateBlock(stack, index), draft);
                Synchronize(graph);
            }
            finally
            {
                graph.UndoEndRecordGraph();
            }
        }

        internal void DuplicateBlock(FeedbackBlockNode block)
        {
            var draft = GetDraft(block);
            if (draft && block.ContextNode is FeedbackStackNode stack && Find(stack.Graph) == this)
            {
                AddBlock(stack, Object.Instantiate(draft), block.Index + 1);
            }
        }

        internal void RemoveBlock(FeedbackBlockNode block)
        {
            var graph = block.Graph;
            if (Find(graph) != this) return;
            graph.UndoBeginRecordGraph("Remove Feedback Block");
            try
            {
                block.ContextNode.RemoveBlockNode(block);
                Synchronize(graph);
            }
            finally
            {
                graph.UndoEndRecordGraph();
            }
        }

        internal FeedbackBlockDraft GetDraft(FeedbackBlockNode block)
        {
            return block.DraftKey != null && drafts.TryGetValue(block.DraftKey, out var entry) &&
                   entry.NodeId == block.ID ? entry.Draft : null;
        }

        internal void OnGraphChanged(Graph graph)
        {
            Synchronize(graph);
            FeedbackGraphWindow.ClearDirty(graphObject);
        }

        internal void Synchronize(Graph graph)
        {
            if (!owner || Undo.isProcessing || inspectorRefreshPending || Find(graph) != this) return;
            var stacks = graph.GetNodes().OfType<FeedbackStackNode>().Take(2).ToArray();
            if (stacks.Length != 1) return;

            var blockDrafts = GetBlockDrafts(stacks[0]);
            if (blockDrafts.Any(draft => !draft || draft.Value == null)) return;
            var state = CaptureBlocks(blockDrafts);
            if (state.SequenceEqual(syncedBlocks)) return;

            using var serializedOwner = new SerializedObject(owner);
            using var property = serializedOwner.FindProperty(propertyPath);
            if (property == null) return;
            if (property.contentHash != propertyHash)
            {
                ScheduleInspectorRefresh();
                return;
            }

            var previousValues = syncedBlocks.Zip(ReadFeedbacks(property), (block, value) => (block.Draft, block.Revision, Value: value))
                .ToDictionary(item => item.Draft);
            var values = state.Select(block => previousValues.TryGetValue(block.Draft, out var previous) &&
                block.Revision == previous.Revision ? previous.Value : block.Draft.CopyValue()).ToArray();
            if (property.isArray)
            {
                property.arraySize = values.Length;
                for (var i = 0; i < values.Length; i++)
                {
                    using var item = property.GetArrayElementAtIndex(i);
                    item.managedReferenceValue = values[i];
                }
            }
            else
            {
                property.managedReferenceValue = values.Length switch
                {
                    0 => null,
                    1 => values[0],
                    _ => new FeedbackSequence(values)
                };
            }
            serializedOwner.ApplyModifiedProperties();
            propertyHash = property.contentHash;
            syncedBlocks = state;
        }

        private void ScheduleInspectorRefresh()
        {
            if (inspectorRefreshPending) return;
            inspectorRefreshPending = true;
            EditorApplication.delayCall += () =>
            {
                try { RefreshFromInspector(); }
                finally { inspectorRefreshPending = false; }
            };
        }

        private void RefreshFromInspector()
        {
            if (!owner || !window || window is not IGraphWindow { Graph: FeedbackGraph graph } || Find(graph) != this) return;
            using var serializedOwner = new SerializedObject(owner);
            using var property = serializedOwner.FindProperty(propertyPath);
            if (property == null) return;

            var values = ReadFeedbacks(property).ToArray();
            var stacks = graph.GetNodes().OfType<FeedbackStackNode>().Take(2).ToArray();
            if (stacks.Length > 1) return;
            var stack = stacks.FirstOrDefault();
            var blockDrafts = stack == null ? Array.Empty<FeedbackBlockDraft>() : GetBlockDrafts(stack);
            if (stack != null && stack.Title == stackTitle && blockDrafts.Length == values.Length &&
                stack.BlockNodes.Select((block, index) => block.Index == index).All(matches => matches) &&
                blockDrafts.Zip(values, (draft, value) => draft && draft.Value != null &&
                    draft.Value.GetType() == value.GetType() &&
                    JsonUtility.ToJson(draft.Value) == JsonUtility.ToJson(value)).All(matches => matches))
            {
                var blockOrderChanged = !blockDrafts.SequenceEqual(syncedBlocks.Select(block => block.Draft));
                syncedBlocks = CaptureBlocks(blockDrafts);
                propertyHash = property.contentHash;
                if (blockOrderChanged) FeedbackGraphCanvas.RefreshGraph(window);
                return;
            }

            if (!FeedbackGraphCanvas.RefreshGraph(window, () =>
                {
                    var position = stack?.Position;
                    if (stack != null) graph.RemoveNode(stack);
                    var replacement = CreateStack(graph);
                    if (position.HasValue) replacement.Position = position.Value;
                    PopulateBlocks(replacement, values);
                    syncedBlocks = CaptureBlocks(GetBlockDrafts(replacement));
                    propertyHash = property.contentHash;
                    FeedbackGraphWindow.ClearDirty(graphObject);
                }))
            {
                window.ShowNotification(new GUIContent("The Inspector field changed. Reopen its graph to continue."));
            }
        }

        private FeedbackBlockDraft[] GetBlockDrafts(FeedbackStackNode stack) =>
            stack.BlockNodes.Cast<FeedbackBlockNode>().Select(GetDraft).ToArray();

        private static (FeedbackBlockDraft Draft, int Revision)[] CaptureBlocks(IEnumerable<FeedbackBlockDraft> blockDrafts) =>
            blockDrafts.Select(draft => (draft, EditorUtility.GetDirtyCount(draft))).ToArray();

        private static IEnumerable<IAction> ReadFeedbacks(SerializedProperty property)
        {
            if (property.isArray)
            {
                for (var i = 0; i < property.arraySize; i++)
                {
                    using var item = property.GetArrayElementAtIndex(i);
                    if (item.managedReferenceValue is IAction value) yield return value;
                }
            }
            else if (property.managedReferenceValue is FeedbackSequence sequence)
            {
                foreach (var value in sequence.Feedbacks.Where(action => action != null)) yield return value;
            }
            else if (property.managedReferenceValue is IAction value)
            {
                yield return value;
            }
        }

        private static bool IsCreatable(Type type) =>
            type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters &&
            !typeof(Object).IsAssignableFrom(type) && type.IsDefined(typeof(SerializableAttribute), false) &&
            type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null) != null;

        private void PopulateBlocks(FeedbackStackNode stack, IEnumerable<IAction> values)
        {
            foreach (var value in values)
            {
                var draft = FeedbackBlockDraft.Create(value);
                try
                {
                    AttachDraft(CreateBlock(stack), Object.Instantiate(draft));
                }
                finally
                {
                    Object.DestroyImmediate(draft);
                }
            }
        }

        private FeedbackStackNode CreateStack(FeedbackGraph graph)
        {
            var stack = new FeedbackStackNode();
            graph.AddNode(stack);
            stack.Title = stackTitle;
            stack.Position = new Vector2(40, 40);
            return stack;
        }

        private static FeedbackBlockNode CreateBlock(FeedbackStackNode stack, int index = -1)
        {
            if (index < 0) index = stack.BlockCount;
            stack.CreateBlockNode<FeedbackBlockNode>(index);
            return (FeedbackBlockNode)stack.GetBlock(index);
        }

        private void AttachDraft(FeedbackBlockNode block, FeedbackBlockDraft draft)
        {
            var key = Guid.NewGuid().ToString("N");
            drafts.Add(key, (block.ID, draft));
            block.DraftKey = key;
            block.Title = ObjectNames.NicifyVariableName(draft.Value.GetType().Name);
        }

        private void ConfigureWindow()
        {
            FeedbackGraphCanvas.Install(window);
            if (window.overlayCanvas.overlays.FirstOrDefault(panel => panel.id == "gtf-asset-management") is { } toolbar)
            {
                window.overlayCanvas.Remove(toolbar);
            }

            // The floating panels can cover the stack when the graph is docked.
            foreach (var panel in window.overlayCanvas.overlays)
            {
                if ((panel.id is "gtf-blackboard" or "gtf-inspector") &&
                    panel.displayed && !panel.collapsed && !collapsedPanels.Contains(panel))
                {
                    panel.collapsed = true;
                    collapsedPanels.Add(panel);
                }
            }
        }

        private void Close()
        {
            var closeWindow = HasOpenWindow;
            Sessions.Remove(graphId);
            if (Sessions.Count == 0) EditorApplication.update -= CloseUnusedSessions;
            foreach (var panel in collapsedPanels)
            {
                if (panel.containerWindow) panel.collapsed = false;
            }
            if (closeWindow) FeedbackGraphWindow.Close(window);
            foreach (var entry in drafts.Values) Object.DestroyImmediate(entry.Draft);
            drafts.Clear();
            if (graphObject) Object.DestroyImmediate(graphObject);
        }

        private static void CloseUnusedSessions()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now < nextWindowCheck) return;
            nextWindowCheck = now + 1;
            foreach (var session in Sessions.Values.ToArray())
            {
                if (!session.HasOpenWindow || !session.owner) session.Close();
            }
        }

        private static void CloseAll()
        {
            foreach (var session in Sessions.Values.ToArray()) session.Close();
        }

        private static void SynchronizeAfterUndo()
        {
            foreach (var session in Sessions.Values) session.ScheduleInspectorRefresh();
        }
    }
}
