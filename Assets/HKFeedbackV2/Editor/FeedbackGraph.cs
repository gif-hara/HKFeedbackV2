using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HKFeedbackV2.Editor
{
    [Serializable]
    [Graph(Extension, GraphOptions.DisableAutoInclusionOfNodesFromGraphAssembly)]
    internal sealed class FeedbackGraph : Graph
    {
        internal const string Extension = "hkfeedbackgraph";

        public override void OnGraphChanged(GraphLogger graphLogger) =>
            FeedbackGraphSession.Find(this)?.OnGraphChanged(this);

        protected override IEnumerable<Type> BuildAvailableVariableTypes(IReadOnlyCollection<Type> supportedTypes) =>
            Array.Empty<Type>();

        protected override IEnumerable<Type> BuildAvailableConstantTypes(IReadOnlyCollection<Type> supportedTypes) =>
            Array.Empty<Type>();
    }

    [Serializable]
    [Node("Feedback Stack", null, null, "Assets/HKFeedbackV2/Editor/FeedbackStackView.uss")]
    [UseWithGraph(typeof(FeedbackGraph))]
    internal sealed class FeedbackStackNode : ContextNode { }

    internal sealed class FeedbackStackView : NodeView<FeedbackStackNode>
    {
        private const float MinimumWidth = 400;

        public override void OnViewBuilt()
        {
            var root = View.Root;
            root.style.minWidth = MinimumWidth;
            if (root.Q(className: "ge-node__port-container") is { } ports) ports.style.flexGrow = 0;
            if (root.Q("blocks-container-part") is { } blocks) blocks.style.flexGrow = 1;
            if (FeedbackGraphSession.Find(Node.Graph) is { } session)
            {
                var size = session.StackSize;
                if (size.x > 0) root.style.width = size.x;
                if (size.y > 0) root.style.minHeight = size.y;
            }

            AddResizeHandle("right", resizeWidth: true, resizeHeight: false);
            AddResizeHandle("bottom", resizeWidth: false, resizeHeight: true);
            AddResizeHandle("corner", resizeWidth: true, resizeHeight: true);

            var addBlock = root.Q("add-block")?.Q<Button>();
            if (addBlock != null)
            {
                addBlock.text = "Add";
                addBlock.clickable = new Clickable(() => FeedbackGraphSession.Find(Node.Graph)?.ShowActionMenu(Node));
            }
        }

        private void AddResizeHandle(string side, bool resizeWidth, bool resizeHeight)
        {
            var root = View.Root;
            var handle = new VisualElement();
            handle.AddToClassList("hk-feedback-resize");
            handle.AddToClassList("hk-feedback-resize--" + side);

            var pointerId = -1;
            var startPosition = Vector2.zero;
            var startSize = Vector2.zero;
            var size = Vector2.zero;
            var resized = false;
            FeedbackGraphSession session = null;
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || pointerId >= 0 || FeedbackGraphSession.Find(Node.Graph) is not { } current) return;
                session = current;
                startPosition = root.parent.WorldToLocal(evt.position);
                startSize = root.layout.size;
                size = session.StackSize;
                resized = false;
                pointerId = evt.pointerId;
                handle.CapturePointer(pointerId);
                evt.StopPropagation();
            });
            // Graph Toolkit starts node moves from the mouse event generated for the pointer.
            handle.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (evt.pointerId != pointerId) return;
                var delta = root.parent.WorldToLocal(evt.position) - startPosition;
                if (resizeWidth) root.style.width = size.x = Mathf.Max(MinimumWidth, startSize.x + delta.x);
                // Keep the stack free to grow when blocks or parameters need more room.
                if (resizeHeight) root.style.minHeight = size.y = Mathf.Max(0, startSize.y + delta.y);
                resized = true;
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId != pointerId || evt.button != 0) return;
                handle.ReleasePointer(pointerId);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                if (evt.pointerId != pointerId) return;
                if (resized) session.SetStackSize(size);
                pointerId = -1;
            });
            root.hierarchy.Add(handle);
        }
    }

    [Serializable]
    [Node("Feedback")]
    [UseWithGraph(typeof(FeedbackGraph))]
    [UseWithContext(typeof(FeedbackStackNode))]
    internal sealed class FeedbackBlockNode : BlockNode
    {
        [SerializeField] internal string DraftKey;
    }

    internal sealed class FeedbackBlockView : NodeView<FeedbackBlockNode>
    {
        internal const string BlockClass = "hk-feedback-block";
        internal const string ParametersName = "hk-feedback-parameters";

        private VisualElement parameters;
        private SerializedObject serializedDraft;

        public override void OnViewBuilt()
        {
            parameters = new VisualElement { name = ParametersName, userData = Node };
            View.Root.AddToClassList(BlockClass);
            View.Root.Add(parameters);
        }

        public override void OnViewAttached()
        {
            if (FeedbackGraphSession.Find(Node.Graph) is { } session)
            {
                Refresh(session);
            }
        }

        public override void OnViewDetached()
        {
            UnbindParameters();
        }

        private void UnbindParameters()
        {
            parameters.Unbind();
            serializedDraft?.Dispose();
            serializedDraft = null;
        }

        private void Refresh(FeedbackGraphSession session)
        {
            UnbindParameters();
            parameters.Clear();
            var draft = session.GetDraft(Node);
            if (!draft)
            {
                parameters.Add(new Label("Action unavailable"));
                return;
            }

            serializedDraft = new SerializedObject(draft);
            using var value = draft.GetValueProperty(serializedDraft);
            var depth = value.depth;
            for (var enterChildren = true; value.NextVisible(enterChildren) && value.depth > depth; enterChildren = false)
            {
                parameters.Add(new PropertyField(value));
            }
            if (parameters.childCount == 0) return;

            parameters.Bind(serializedDraft);
            parameters.TrackSerializedObjectValue(serializedDraft, _ => session.Synchronize(Node.Graph));
        }
    }

    internal static class FeedbackGraphCanvas
    {
        private const string GraphViewClass = "ge-graph-view";

        internal static void Install(EditorWindow window)
        {
            const string installedClass = "hk-feedback-canvas";
            var canvasContainer = window.rootVisualElement.Q(className: GraphViewClass)?.parent;
            if (canvasContainer == null || canvasContainer.ClassListContains(installedClass)) return;
            canvasContainer.AddToClassList(installedClass);

            bool IsFeedbackGraph() => window && window is IGraphWindow { Graph: FeedbackGraph graph } &&
                FeedbackGraphSession.Find(graph) != null;
            var toolkitAssembly = typeof(Graph).Assembly;
            var graphElement = toolkitAssembly.GetType("Unity.GraphToolkit.Editor.GraphElement");
            var isSelected = graphElement?.GetMethod("IsSelected",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            void RestrictCommand(string command, EventBase evt)
            {
                if (!IsFeedbackGraph() || IsParameterField(evt.target)) return;

                var removesSelection = command is "Cut" or "Delete" or "SoftDelete";
                if (command is "Paste" or "Duplicate" || removesSelection &&
                    canvasContainer.Q(className: "ge-context-node") is { } stack && isSelected?.Invoke(stack, null) is true)
                {
                    evt.StopImmediatePropagation();
                }
            }

            canvasContainer.RegisterCallback<ValidateCommandEvent>(
                evt => RestrictCommand(evt.commandName, evt), TrickleDown.TrickleDown);
            canvasContainer.RegisterCallback<ExecuteCommandEvent>(
                evt => RestrictCommand(evt.commandName, evt), TrickleDown.TrickleDown);
            canvasContainer.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (IsFeedbackGraph() && !IsParameterField(evt.target))
                {
                    evt.StopImmediatePropagation();
                }
            }, TrickleDown.TrickleDown);
            canvasContainer.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                if (IsFeedbackGraph()) ShowBlockMenu(evt, canvasContainer);
            }, TrickleDown.TrickleDown);

            // Graph Toolkit dispatches its own events for these shortcuts, including remapped bindings.
            foreach (var name in new[]
                     {
                         "ShortcutShowItemLibraryEvent", "ShortcutCreateStickyNoteEvent",
                         "ShortcutCreatePlacematEvent", "ShortcutCreateLocalSubgraphFromSelectionEvent",
                         "ShortCutPasteWithoutWires", "ShortCutDuplicateWithoutWires"
                     })
            {
                RegisterShortcut(canvasContainer, name, evt =>
                {
                    if (IsFeedbackGraph()) evt.StopImmediatePropagation();
                });
            }

            var root = window.rootVisualElement;
            const string saveInstalledClass = "hk-feedback-save-shortcut";
            if (root.ClassListContains(saveInstalledClass)) return;
            root.AddToClassList(saveInstalledClass);
            RegisterShortcut(root, "ShortcutSaveAsEvent", evt =>
            {
                if (IsFeedbackGraph()) evt.StopImmediatePropagation();
            });
            RegisterShortcut(root, "ShortcutFileSaveEvent", evt =>
            {
                if (window is not IGraphWindow { Graph: FeedbackGraph graph } ||
                    FeedbackGraphSession.Find(graph) is not { } session) return;

                evt.StopImmediatePropagation();
                if (root.focusController?.focusedElement is VisualElement focused && IsParameterField(focused))
                {
                    focused.Blur();
                }
                root.schedule.Execute(() => session.Save(graph));
            });
        }

        internal static bool RefreshGraph(EditorWindow window, Action updateBlocks = null)
        {
            // The Inspector value can be restored independently of the temporary graph's undo state.
            // Use the view's update scope to redraw the graph without recording a second edit.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var canvas = window.rootVisualElement.Q(className: GraphViewClass);
            var viewType = typeof(Graph).Assembly.GetType("Unity.GraphToolkit.Editor.GraphView");
            var viewModel = canvas == null ? null : viewType?.GetProperty("GraphViewModel", flags)?.GetValue(canvas);
            var graphState = viewModel?.GetType().GetProperty("GraphModelState", flags)?.GetValue(viewModel);
            using var updater = graphState?.GetType().GetProperty("UpdateScope", flags)?.GetValue(graphState) as IDisposable;
            var refreshView = updater?.GetType().GetMethod("ForceCompleteUpdate", flags);
            if (refreshView == null) return false;

            updateBlocks?.Invoke();
            refreshView.Invoke(updater, null);
            return true;
        }

        private static void ShowBlockMenu(ContextualMenuPopulateEvent evt, VisualElement canvas)
        {
            if (IsParameterField(evt.target)) return;

            evt.StopImmediatePropagation();
            evt.menu.MenuItems().Clear();
            var target = evt.triggerEvent?.target as VisualElement ?? canvas.panel?.Pick(evt.mousePosition);
            var blockView = FindAncestor(target, element => element.ClassListContains(FeedbackBlockView.BlockClass));
            if (blockView?.Q(FeedbackBlockView.ParametersName)?.userData is not FeedbackBlockNode block ||
                FeedbackGraphSession.Find(block.Graph) is not { } session) return;

            evt.menu.AppendAction("Duplicate Block", _ => session.DuplicateBlock(block));
            evt.menu.AppendAction("Remove Block", _ => session.RemoveBlock(block));
        }

        private static bool IsParameterField(IEventHandler target) =>
            FindAncestor(target as VisualElement, element => element.name == FeedbackBlockView.ParametersName) != null;

        private static VisualElement FindAncestor(VisualElement element, Func<VisualElement, bool> matches)
        {
            for (; element != null; element = element.parent)
            {
                if (matches(element)) return element;
            }
            return null;
        }

        private static void RegisterShortcut(VisualElement element, string name, Action<EventBase> callback)
        {
            var eventType = typeof(Graph).Assembly.GetType("Unity.GraphToolkit.Editor." + name);
            if (eventType?.GetConstructor(Type.EmptyTypes) == null) return;
            typeof(FeedbackGraphCanvas).GetMethod(nameof(RegisterShortcutCallback), BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(eventType).Invoke(null, new object[] { element, callback });
        }

        private static void RegisterShortcutCallback<T>(VisualElement element, Action<EventBase> callback)
            where T : EventBase<T>, new() =>
            element.RegisterCallback<T>(evt => callback(evt), TrickleDown.TrickleDown);
    }
}
