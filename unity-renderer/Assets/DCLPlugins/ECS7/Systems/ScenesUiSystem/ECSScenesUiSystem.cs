using System;
using System.Collections.Generic;
using DCL;
using DCL.Controllers;
using DCL.ECS7.InternalComponents;
using DCL.ECSRuntime;
using DCL.Models;
using UnityEngine.UIElements;

namespace ECSSystems.ScenesUiSystem
{
    public class ECSScenesUiSystem : IDisposable
    {
        private class State
        {
            public UIDocument uiDocument;
            public IInternalECSComponent<InternalUiContainer> internalUiContainerComponent;
            public IWorldState worldState;
            public BaseList<IParcelScene> loadedScenes;
            public int lastSceneNumber;
            public bool isPendingSceneUI;
            public IParcelScene currentScene;
        }

        private readonly State state;

        public ECSScenesUiSystem(UIDocument uiDocument,
            IInternalECSComponent<InternalUiContainer> internalUiContainerComponent,
            BaseList<IParcelScene> loadedScenes,
            IWorldState worldState)
        {
            state = new State()
            {
                uiDocument = uiDocument,
                internalUiContainerComponent = internalUiContainerComponent,
                worldState = worldState,
                loadedScenes = loadedScenes,
                lastSceneNumber = -1,
                isPendingSceneUI = true,
                currentScene = null
            };

            state.loadedScenes.OnRemoved += LoadedScenesOnOnRemoved;
        }

        public void Dispose()
        {
            state.loadedScenes.OnRemoved -= LoadedScenesOnOnRemoved;
        }

        public void Update()
        {
            int currentSceneNumber = state.worldState.GetCurrentSceneNumber();
            bool sceneChanged = state.lastSceneNumber != currentSceneNumber;
            state.lastSceneNumber = currentSceneNumber;

            HashSet<IParcelScene> scenesUiToSort = ApplyParenting(state.uiDocument, state.internalUiContainerComponent, currentSceneNumber);

            // If parenting detects that the order for ui elements has changed, it should sort the ui tree
            if (scenesUiToSort.Count > 0)
            {
                SortSceneUiTree(state.internalUiContainerComponent, scenesUiToSort);
            }

            // clear UI if scene changed
            if (sceneChanged && !state.isPendingSceneUI)
            {
                ClearCurrentSceneUI(state.uiDocument);
                state.isPendingSceneUI = currentSceneNumber > 0;
            }
            
            if (sceneChanged && state.currentScene != null && currentSceneNumber != state.currentScene.sceneData.sceneNumber)
            {
                state.currentScene = null;
            }

            // UI not set for current scene yet
            if (state.isPendingSceneUI)
            {
                // we get current scene reference
                state.currentScene ??= GetCurrentScene(currentSceneNumber, state.loadedScenes);

                // we apply current scene UI
                if (state.currentScene != null)
                {
                    if (ApplySceneUI(state.internalUiContainerComponent, state.uiDocument, state.currentScene))
                    {
                        state.isPendingSceneUI = false;
                    }
                }
            }
        }

        private void LoadedScenesOnOnRemoved(IParcelScene scene)
        {
            if (scene.sceneData.sceneNumber == state.lastSceneNumber)
            {
                state.lastSceneNumber = -1;
            }
        }

        internal static HashSet<IParcelScene> ApplyParenting(UIDocument uiDocument,
            IInternalECSComponent<InternalUiContainer> internalUiContainerComponent, int currentSceneNumber)
        {
            HashSet<IParcelScene> scenesToSort = new HashSet<IParcelScene>();

            // check for orphan ui containers
            var allContainers = internalUiContainerComponent.GetForAll();
            for (int i = 0; i < allContainers.Count; i++)
            {
                var uiContainerData = allContainers[i].value;

                // check if ui should be sort (only current and global scenes)
                if (uiContainerData.model.shouldSort
                    && (uiContainerData.scene.isPersistent || uiContainerData.scene.sceneData.sceneNumber == currentSceneNumber))
                {
                    scenesToSort.Add(uiContainerData.scene);
                }

                // add global scenes ui but
                // skip non-global scenes ui containers on root entity since no parenting is needed
                if (uiContainerData.entity.entityId == SpecialEntityId.SCENE_ROOT_ENTITY)
                {
                    var model = uiContainerData.model;
                    if (uiContainerData.scene.isPersistent && model.parentElement == null)
                    {
                        uiDocument.rootVisualElement.Add(model.rootElement);
                        model.parentElement = uiDocument.rootVisualElement;
                        internalUiContainerComponent.PutFor(uiContainerData.scene, uiContainerData.entity, model);
                    }
                    continue;
                }

                // skip containers with parent already set
                if (uiContainerData.model.parentElement != null)
                    continue;

                InternalUiContainer parentDataModel = GetParentContainerModel(
                    internalUiContainerComponent,
                    uiContainerData.scene,
                    uiContainerData.model.parentId);

                // apply parenting
                if (parentDataModel != null)
                {
                    var currentContainerModel = uiContainerData.model;
                    parentDataModel.rootElement.Add(uiContainerData.model.rootElement);
                    currentContainerModel.parentElement = parentDataModel.rootElement;

                    internalUiContainerComponent.PutFor(uiContainerData.scene, uiContainerData.model.parentId, parentDataModel);
                    internalUiContainerComponent.PutFor(uiContainerData.scene, uiContainerData.entity, currentContainerModel);
                }
            }
            return scenesToSort;
        }

        internal static void ClearCurrentSceneUI(UIDocument uiDocument)
        {
            if (uiDocument.rootVisualElement.childCount > 0)
            {
                uiDocument.rootVisualElement.RemoveAt(0);
            }
        }

        internal static IParcelScene GetCurrentScene(int sceneNumber, IList<IParcelScene> loadedScenes)
        {
            if (sceneNumber <= 0)
                return null;

            IParcelScene currentScene = null;
            for (int i = 0; i < loadedScenes.Count; i++)
            {
                if (loadedScenes[i].sceneData.sceneNumber == sceneNumber)
                {
                    currentScene = loadedScenes[i];
                    break;
                }
            }

            return currentScene;
        }

        internal static bool ApplySceneUI(IInternalECSComponent<InternalUiContainer> internalUiContainerComponent,
            UIDocument uiDocument, IParcelScene currentScene)
        {
            IECSReadOnlyComponentData<InternalUiContainer> sceneRootUiContainer =
                internalUiContainerComponent.GetFor(currentScene, SpecialEntityId.SCENE_ROOT_ENTITY);

            if (sceneRootUiContainer != null)
            {
                var model = sceneRootUiContainer.model;
                uiDocument.rootVisualElement.Insert(0, model.rootElement);
                model.parentElement = uiDocument.rootVisualElement;
                internalUiContainerComponent.PutFor(currentScene, SpecialEntityId.SCENE_ROOT_ENTITY, model);
                return true;
            }
            return false;
        }

        private static InternalUiContainer GetParentContainerModel(IInternalECSComponent<InternalUiContainer> internalUiContainerComponent,
            IParcelScene scene, long parentId)
        {
            InternalUiContainer parentDataModel =
                internalUiContainerComponent.GetFor(scene, parentId)?.model;

            // create root entity ui container if needed
            if (parentDataModel == null && parentId == SpecialEntityId.SCENE_ROOT_ENTITY)
            {
                parentDataModel = new InternalUiContainer();
            }
            return parentDataModel;
        }

        internal static void SortSceneUiTree(IInternalECSComponent<InternalUiContainer> internalUiContainerComponent,
            ICollection<IParcelScene> scenesToSort)
        {
            Dictionary<VisualElement, Dictionary<long, RightOfData>> sortSceneTree = new Dictionary<VisualElement, Dictionary<long, RightOfData>>();

            // Setup UI sorting
            var allContainers = internalUiContainerComponent.GetForAll();
            for (int i = 0; i < allContainers.Count; i++)
            {
                var uiContainerData = allContainers[i].value;

                if (!scenesToSort.Contains(uiContainerData.scene))
                    continue;

                InternalUiContainer model = uiContainerData.model;

                // If not parented yet, we skip it
                if (model.parentElement == null)
                    continue;

                // Ignore root scene UI container
                if (uiContainerData.entity.entityId == SpecialEntityId.SCENE_ROOT_ENTITY)
                    continue;

                if (!sortSceneTree.TryGetValue(model.parentElement, out var sceneTreeDictionary))
                {
                    sceneTreeDictionary = new Dictionary<long, RightOfData>();
                    sortSceneTree[model.parentElement] = sceneTreeDictionary;
                }

                sceneTreeDictionary[model.rigthOf] = new RightOfData(model.rootElement,
                    uiContainerData.entity.entityId);

                model.shouldSort = false;
                internalUiContainerComponent.PutFor(uiContainerData.scene, uiContainerData.entity.entityId, model);
            }

            // Apply UI sorting
            foreach (var pairs in sortSceneTree)
            {
                VisualElement parentElement = pairs.Key;
                Dictionary<long, RightOfData> sceneSort = pairs.Value;

                int index = 0;
                long nextElementId = 0;

                while (sceneSort.TryGetValue(nextElementId, out RightOfData rightOfData))
                {
                    sceneSort.Remove(nextElementId);
                    parentElement.Remove(rightOfData.element);
                    parentElement.Insert(index, rightOfData.element);
                    nextElementId = rightOfData.id;
                    index++;
                }
            }
        }

        private readonly struct RightOfData
        {
            public readonly long id;
            public readonly VisualElement element;

            public RightOfData(VisualElement element, long id)
            {
                this.id = id;
                this.element = element;
            }
        }
    }
}