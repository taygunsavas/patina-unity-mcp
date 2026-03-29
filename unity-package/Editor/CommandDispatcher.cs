using System.Collections.Concurrent;
using System.Threading.Tasks;
using Patina.Editor.Commands;
using UnityEngine;

namespace Patina.Editor
{
    public static class CommandDispatcher
    {
        private static readonly ConcurrentDictionary<string, ICommandHandler> _handlers = new ConcurrentDictionary<string, ICommandHandler>();

        public static void RegisterHandler(string command, ICommandHandler handler)
        {
            _handlers[command] = handler;
        }

        public static async Task<BridgeResponse> Dispatch(BridgeRequest request)
        {
            if (request == null)
                return BridgeResponse.Fail(null, "Null request");

            string id = request.Id;
            string command = request.Command;

            if (string.IsNullOrEmpty(command))
                return BridgeResponse.Fail(id, "Missing command field");

            if (!_handlers.TryGetValue(command, out var handler))
                return BridgeResponse.Fail(id, "Unknown command: " + command);

            try
            {
                object result = await handler.HandleAsync(request.Parameters);
                return BridgeResponse.Ok(id, result);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Patina] Error handling command '{command}': {ex}");
                return BridgeResponse.Fail(id, ex.Message);
            }
        }

        public static void RegisterBuiltInHandlers()
        {
            // Phase 1
            RegisterHandler("log_to_console", new LogToConsoleHandler());
            RegisterHandler("get_hierarchy", new GetHierarchyHandler());
            RegisterHandler("create_game_object", new CreateGameObjectHandler());
            // Phase 2 — Scene
            RegisterHandler("get_scene_info", new GetSceneInfoHandler());
            // Phase 2 — Components
            RegisterHandler("add_component", new AddComponentHandler());
            RegisterHandler("set_property", new SetPropertyHandler());
            RegisterHandler("remove_component", new RemoveComponentHandler());
            // Phase 2 — Hierarchy ops
            RegisterHandler("reparent_game_object", new ReparentGameObjectHandler());
            RegisterHandler("delete_game_object", new DeleteGameObjectHandler());
            RegisterHandler("duplicate_game_object", new DuplicateGameObjectHandler());
            // Phase 2 — Prefabs
            RegisterHandler("create_prefab", new CreatePrefabHandler());
            RegisterHandler("instantiate_prefab", new InstantiatePrefabHandler());
            // Issue #26 — Advanced prefab workflow
            RegisterHandler("get_prefab_info", new GetPrefabInfoHandler());
            RegisterHandler("unpack_prefab", new UnpackPrefabHandler());
            RegisterHandler("apply_prefab_overrides", new ApplyPrefabOverridesHandler());
            RegisterHandler("revert_prefab_overrides", new RevertPrefabOverridesHandler());
            // Phase 2 — Assets
            RegisterHandler("find_assets_by_type", new FindAssetsByTypeHandler());
            RegisterHandler("find_assets_by_name", new FindAssetsByNameHandler());
            // Phase 5 — Asset database operations
            RegisterHandler("create_folder", new CreateFolderHandler());
            RegisterHandler("move_asset", new MoveAssetHandler());
            RegisterHandler("rename_asset", new RenameAssetHandler());
            RegisterHandler("delete_asset", new DeleteAssetHandler());
            RegisterHandler("get_asset_info", new GetAssetInfoHandler());
            RegisterHandler("refresh_asset_database", new RefreshAssetDatabaseHandler());
            RegisterHandler("set_asset_labels", new SetAssetLabelsHandler());
            // Phase 4 — Inspection and spatial search
            RegisterHandler("get_game_object_info", new GetGameObjectInfoHandler());
            RegisterHandler("find_game_objects_by_tag", new FindGameObjectsByTagHandler());
            RegisterHandler("find_game_objects_by_component", new FindGameObjectsByComponentHandler());
            RegisterHandler("find_game_objects_by_layer", new FindGameObjectsByLayerHandler());
            // Phase 3 — Scene lifecycle
            RegisterHandler("open_scene", new OpenSceneHandler());
            RegisterHandler("save_scene", new SaveSceneHandler());
            RegisterHandler("new_scene", new NewSceneHandler());
            RegisterHandler("get_build_settings", new GetBuildSettingsHandler());
            RegisterHandler("set_build_scenes", new SetBuildScenesHandler());
            // Sprint 4 — Scripting
            RegisterHandler("create_script", new CreateScriptHandler());
            // Issue #24 — Material and shader authoring
            RegisterHandler("create_material", new CreateMaterialHandler());
            RegisterHandler("set_material_property", new SetMaterialPropertyHandler());
            RegisterHandler("get_material_properties", new GetMaterialPropertiesHandler());
            RegisterHandler("assign_material", new AssignMaterialHandler());
            // Issue #25 — Editor state and control
            RegisterHandler("get_editor_state", new GetEditorStateHandler());
            RegisterHandler("set_play_mode", new SetPlayModeHandler());
            RegisterHandler("get_console_logs", new GetConsoleLogsHandler());
            RegisterHandler("execute_menu_item", new ExecuteMenuItemHandler());
            RegisterHandler("clear_console", new ClearConsoleHandler());
            // Issue #27 — Build and player settings tools
            RegisterHandler("get_player_settings", new GetPlayerSettingsHandler());
            RegisterHandler("set_player_settings", new SetPlayerSettingsHandler());
            RegisterHandler("set_build_target", new SetBuildTargetHandler());
        }

        public static int HandlerCount
        {
            get { return _handlers.Count; }
        }
    }
}
