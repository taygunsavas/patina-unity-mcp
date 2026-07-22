use schemars::JsonSchema;
use serde_json::{json, Value};

use crate::tools::{
    AddComponentArgs, ApplyPrefabOverridesArgs, AssignMaterialArgs, BatchAddComponentsArgs,
    BatchSetPropertiesArgs, BatchSetTransformArgs, BeginUndoGroupArgs, ClearConsoleArgs,
    ClosePrefabStageArgs, CompileAndGetErrorsArgs, CreateFolderArgs, CreateGameObjectArgs,
    CreateMaterialArgs, CreatePrefabArgs, CreateScriptArgs, DeleteAssetArgs, DeleteGameObjectArgs,
    DuplicateGameObjectArgs, EditPrefabAssetArgs, EndUndoGroupArgs, ExecuteMenuItemArgs,
    FindAssetsByNameArgs, FindAssetsByTypeArgs, FindGameObjectsByComponentArgs,
    FindGameObjectsByLayerArgs, FindGameObjectsByPathArgs, FindGameObjectsByTagArgs,
    ForceRecompileArgs, GetAnimatorInfoArgs, GetAssemblyTypesArgs, GetAssetInfoArgs,
    GetBuildSettingsArgs, GetCompilationErrorsArgs, GetConsoleLogsArgs, GetEditorStateArgs,
    GetGameObjectComponentsArgs, GetGameObjectInfoArgs, GetHierarchyArgs,
    GetMaterialPropertiesArgs, GetPlayerSettingsArgs, GetPrefabInfoArgs, GetProjectSettingsArgs,
    GetSceneInfoArgs, GetSceneStatsArgs, GetScriptContentArgs, GetScriptableObjectArgs,
    GetSelectionArgs, GetTestListArgs, GetTestResultsArgs, GetUndoStackArgs, InstantiatePrefabArgs,
    ListAnimationClipsArgs, ListPrefabComponentsArgs, LogToConsoleArgs, MoveAssetArgs,
    NewSceneArgs, OpenPrefabStageArgs, OpenSceneArgs, QueryGameObjectsArgs, RedoArgs,
    RefreshAssetDatabaseArgs, RemoveComponentArgs, RenameAssetArgs, ReparentGameObjectArgs,
    ResolveScriptTypeArgs, RevertPrefabOverridesArgs, RunTestsArgs, SaveSceneArgs,
    SetActiveStateArgs, SetAnimatorParameterArgs, SetAssetLabelsArgs, SetBuildScenesArgs,
    SetBuildTargetArgs, SetLayerArgs, SetMaterialPropertyArgs, SetPlayModeArgs,
    SetPlayerSettingsArgs, SetPropertyArgs, SetScriptableObjectFieldArgs, SetSelectionArgs,
    SetTagArgs, SetTransformArgs, UndoArgs, UnpackPrefabArgs, ValidateAssetsArgs,
    ValidateSceneArgs,
};

pub struct CommandSpec {
    pub name: &'static str,
    pub category: &'static str,
    pub description: &'static str,
    schema: fn() -> Value,
}

impl CommandSpec {
    pub fn summary(&self, include_schema: bool) -> Value {
        let mut value = json!({
            "name": self.name,
            "category": self.category,
            "description": self.description,
        });

        if include_schema {
            value["parametersSchema"] = (self.schema)();
        }

        value
    }
}

fn schema_for<T: JsonSchema>() -> Value {
    serde_json::to_value(schemars::schema_for!(T)).unwrap_or_else(|_| json!({}))
}

macro_rules! command {
    ($name:literal, $category:literal, $args:ty, $description:literal) => {
        CommandSpec {
            name: $name,
            category: $category,
            description: $description,
            schema: schema_for::<$args>,
        }
    };
}

pub fn command_count() -> usize {
    COMMANDS.len()
}

pub fn find_command(name: &str) -> Option<&'static CommandSpec> {
    COMMANDS.iter().find(|command| command.name == name)
}

pub fn categories() -> Vec<&'static str> {
    let mut values = COMMANDS
        .iter()
        .map(|command| command.category)
        .collect::<Vec<_>>();
    values.sort_unstable();
    values.dedup();
    values
}

pub fn query(
    category: Option<&str>,
    search: Option<&str>,
    command: Option<&str>,
    include_schema: bool,
) -> Value {
    let normalized_search = search.map(|value| value.to_lowercase());
    let commands = COMMANDS
        .iter()
        .filter(|spec| command.is_none_or(|name| spec.name == name))
        .filter(|spec| category.is_none_or(|value| spec.category == value))
        .filter(|spec| {
            normalized_search.as_ref().is_none_or(|needle| {
                spec.name.contains(needle)
                    || spec.category.contains(needle)
                    || spec.description.to_lowercase().contains(needle)
            })
        })
        .map(|spec| spec.summary(include_schema))
        .collect::<Vec<_>>();

    json!({
        "commandCount": command_count(),
        "returnedCount": commands.len(),
        "categories": categories(),
        "commands": commands,
    })
}

static COMMANDS: [CommandSpec; 86] = [
    command!(
        "log_to_console",
        "console",
        LogToConsoleArgs,
        "Emit a message to the Unity Editor Console."
    ),
    command!(
        "get_hierarchy",
        "hierarchy",
        GetHierarchyArgs,
        "Return the active scene GameObject tree as nested JSON."
    ),
    command!(
        "create_game_object",
        "gameobject",
        CreateGameObjectArgs,
        "Create an empty GameObject or built-in primitive."
    ),
    command!(
        "get_scene_info",
        "scene",
        GetSceneInfoArgs,
        "Return metadata for the active scene."
    ),
    command!(
        "add_component",
        "component",
        AddComponentArgs,
        "Add a Unity component to a named GameObject."
    ),
    command!(
        "set_property",
        "component",
        SetPropertyArgs,
        "Set a serialized property on a component."
    ),
    command!(
        "remove_component",
        "component",
        RemoveComponentArgs,
        "Remove a component from a named GameObject."
    ),
    command!(
        "reparent_game_object",
        "hierarchy",
        ReparentGameObjectArgs,
        "Move a GameObject under a new parent or scene root."
    ),
    command!(
        "delete_game_object",
        "hierarchy",
        DeleteGameObjectArgs,
        "Delete a named GameObject and its children."
    ),
    command!(
        "duplicate_game_object",
        "hierarchy",
        DuplicateGameObjectArgs,
        "Duplicate a named GameObject and its children."
    ),
    command!(
        "create_prefab",
        "prefab",
        CreatePrefabArgs,
        "Save a scene GameObject as a prefab asset."
    ),
    command!(
        "instantiate_prefab",
        "prefab",
        InstantiatePrefabArgs,
        "Instantiate a prefab into the active scene."
    ),
    command!(
        "get_prefab_info",
        "prefab",
        GetPrefabInfoArgs,
        "Inspect a prefab asset or scene prefab instance."
    ),
    command!(
        "unpack_prefab",
        "prefab",
        UnpackPrefabArgs,
        "Unpack a scene prefab instance."
    ),
    command!(
        "apply_prefab_overrides",
        "prefab",
        ApplyPrefabOverridesArgs,
        "Apply scene prefab instance overrides to its source asset."
    ),
    command!(
        "revert_prefab_overrides",
        "prefab",
        RevertPrefabOverridesArgs,
        "Revert scene prefab instance overrides."
    ),
    command!(
        "find_assets_by_type",
        "asset",
        FindAssetsByTypeArgs,
        "Search the Asset Database by Unity type filter."
    ),
    command!(
        "find_assets_by_name",
        "asset",
        FindAssetsByNameArgs,
        "Search the Asset Database by partial name."
    ),
    command!(
        "create_folder",
        "asset",
        CreateFolderArgs,
        "Create a folder in the Asset Database."
    ),
    command!(
        "move_asset",
        "asset",
        MoveAssetArgs,
        "Move an asset to a new project-relative path."
    ),
    command!(
        "rename_asset",
        "asset",
        RenameAssetArgs,
        "Rename an asset in place."
    ),
    command!(
        "delete_asset",
        "asset",
        DeleteAssetArgs,
        "Delete an asset by project-relative path."
    ),
    command!(
        "get_asset_info",
        "asset",
        GetAssetInfoArgs,
        "Return metadata for an asset."
    ),
    command!(
        "refresh_asset_database",
        "asset",
        RefreshAssetDatabaseArgs,
        "Trigger AssetDatabase.Refresh."
    ),
    command!(
        "set_asset_labels",
        "asset",
        SetAssetLabelsArgs,
        "Replace labels on an asset."
    ),
    command!(
        "get_game_object_info",
        "inspection",
        GetGameObjectInfoArgs,
        "Return detailed data for a named GameObject."
    ),
    command!(
        "get_game_object_components",
        "inspection",
        GetGameObjectComponentsArgs,
        "Return a lightweight component list for a GameObject."
    ),
    command!(
        "find_game_objects_by_tag",
        "inspection",
        FindGameObjectsByTagArgs,
        "Find active GameObjects with a tag."
    ),
    command!(
        "find_game_objects_by_component",
        "inspection",
        FindGameObjectsByComponentArgs,
        "Find scene objects with a component type."
    ),
    command!(
        "find_game_objects_by_layer",
        "inspection",
        FindGameObjectsByLayerArgs,
        "Find scene objects on a layer."
    ),
    command!(
        "open_scene",
        "scene",
        OpenSceneArgs,
        "Open a scene by project-relative path."
    ),
    command!(
        "save_scene",
        "scene",
        SaveSceneArgs,
        "Save the active scene or a scene at a path."
    ),
    command!(
        "new_scene",
        "scene",
        NewSceneArgs,
        "Create and save a new scene."
    ),
    command!(
        "get_build_settings",
        "scene",
        GetBuildSettingsArgs,
        "Return project Build Settings."
    ),
    command!(
        "set_build_scenes",
        "scene",
        SetBuildScenesArgs,
        "Replace the Build Settings scene list."
    ),
    command!(
        "create_script",
        "script",
        CreateScriptArgs,
        "Create a C# script file in the Unity project."
    ),
    command!(
        "create_material",
        "material",
        CreateMaterialArgs,
        "Create a Material asset."
    ),
    command!(
        "set_material_property",
        "material",
        SetMaterialPropertyArgs,
        "Set a shader property on a Material asset."
    ),
    command!(
        "get_material_properties",
        "material",
        GetMaterialPropertiesArgs,
        "Read exposed shader properties from a Material."
    ),
    command!(
        "assign_material",
        "material",
        AssignMaterialArgs,
        "Assign a Material asset to a Renderer slot."
    ),
    command!(
        "get_editor_state",
        "editor",
        GetEditorStateArgs,
        "Return current Unity Editor state."
    ),
    command!(
        "set_play_mode",
        "editor",
        SetPlayModeArgs,
        "Enter, exit, pause, unpause, or step play mode."
    ),
    command!(
        "get_console_logs",
        "console",
        GetConsoleLogsArgs,
        "Read buffered Unity console log entries."
    ),
    command!(
        "execute_menu_item",
        "editor",
        ExecuteMenuItemArgs,
        "Execute a Unity Editor menu item."
    ),
    command!(
        "clear_console",
        "console",
        ClearConsoleArgs,
        "Clear Unity Editor console log entries."
    ),
    command!(
        "get_player_settings",
        "build",
        GetPlayerSettingsArgs,
        "Read Unity Player Settings."
    ),
    command!(
        "set_player_settings",
        "build",
        SetPlayerSettingsArgs,
        "Write Unity Player Settings fields."
    ),
    command!(
        "set_build_target",
        "build",
        SetBuildTargetArgs,
        "Switch the active Unity build target."
    ),
    command!(
        "get_selection",
        "selection",
        GetSelectionArgs,
        "Return the current Editor selection."
    ),
    command!(
        "set_selection",
        "selection",
        SetSelectionArgs,
        "Set the Editor selection."
    ),
    command!(
        "set_active_state",
        "gameobject",
        SetActiveStateArgs,
        "Set active state on a GameObject."
    ),
    command!(
        "set_tag",
        "gameobject",
        SetTagArgs,
        "Set the tag on a GameObject."
    ),
    command!(
        "set_layer",
        "gameobject",
        SetLayerArgs,
        "Set the layer on a GameObject."
    ),
    command!(
        "set_transform",
        "gameobject",
        SetTransformArgs,
        "Set position, rotation, or scale on a GameObject."
    ),
    command!(
        "get_project_settings",
        "project",
        GetProjectSettingsArgs,
        "Return a read-only project settings snapshot."
    ),
    command!(
        "batch_set_properties",
        "batch",
        BatchSetPropertiesArgs,
        "Apply property mutations across multiple GameObjects."
    ),
    command!(
        "batch_add_components",
        "batch",
        BatchAddComponentsArgs,
        "Add components to multiple GameObjects."
    ),
    command!(
        "batch_set_transform",
        "batch",
        BatchSetTransformArgs,
        "Apply transform changes to multiple GameObjects."
    ),
    command!(
        "query_game_objects",
        "query",
        QueryGameObjectsArgs,
        "Find GameObjects matching compound filters."
    ),
    command!(
        "find_game_objects_by_path",
        "query",
        FindGameObjectsByPathArgs,
        "Find GameObjects by hierarchy path prefix."
    ),
    command!(
        "begin_undo_group",
        "undo",
        BeginUndoGroupArgs,
        "Open a named Undo group."
    ),
    command!(
        "end_undo_group",
        "undo",
        EndUndoGroupArgs,
        "Collapse operations into an Undo group."
    ),
    command!("undo", "undo", UndoArgs, "Perform one or more Undo steps."),
    command!("redo", "undo", RedoArgs, "Perform one or more Redo steps."),
    command!(
        "get_undo_stack",
        "undo",
        GetUndoStackArgs,
        "Return current Undo and Redo stack entry names."
    ),
    command!(
        "get_compilation_errors",
        "script",
        GetCompilationErrorsArgs,
        "Return compilation errors and warnings."
    ),
    command!(
        "get_script_content",
        "script",
        GetScriptContentArgs,
        "Read a Unity C# script asset."
    ),
    command!(
        "get_assembly_types",
        "script",
        GetAssemblyTypesArgs,
        "List public types in a Unity assembly."
    ),
    command!(
        "force_recompile",
        "script",
        ForceRecompileArgs,
        "Trigger a Unity script recompile."
    ),
    command!(
        "validate_scene",
        "validation",
        ValidateSceneArgs,
        "Scan the active scene for quality issues."
    ),
    command!(
        "get_scene_stats",
        "validation",
        GetSceneStatsArgs,
        "Return lightweight active scene statistics."
    ),
    command!(
        "get_scriptable_object",
        "scriptable_object",
        GetScriptableObjectArgs,
        "Read serialized fields from a ScriptableObject asset."
    ),
    command!(
        "set_scriptable_object_field",
        "scriptable_object",
        SetScriptableObjectFieldArgs,
        "Set one serialized ScriptableObject field."
    ),
    command!(
        "run_tests",
        "test",
        RunTestsArgs,
        "Start a Unity Test Runner execution."
    ),
    command!(
        "get_test_results",
        "test",
        GetTestResultsArgs,
        "Return results from the most recent test run."
    ),
    command!(
        "get_test_list",
        "test",
        GetTestListArgs,
        "List available Unity tests."
    ),
    command!(
        "get_animator_info",
        "animation",
        GetAnimatorInfoArgs,
        "Read Animator Controller parameters and state info."
    ),
    command!(
        "set_animator_parameter",
        "animation",
        SetAnimatorParameterArgs,
        "Set an Animator parameter in play mode."
    ),
    command!(
        "list_animation_clips",
        "animation",
        ListAnimationClipsArgs,
        "List AnimationClip assets in the project."
    ),
    command!(
        "compile_and_get_errors",
        "script",
        CompileAndGetErrorsArgs,
        "Trigger compilation and return compiler errors only."
    ),
    command!(
        "resolve_script_type",
        "script",
        ResolveScriptTypeArgs,
        "Resolve a MonoScript GUID by fully qualified C# type."
    ),
    command!(
        "list_prefab_components",
        "prefab",
        ListPrefabComponentsArgs,
        "List components on a prefab asset."
    ),
    command!(
        "edit_prefab_asset",
        "prefab",
        EditPrefabAssetArgs,
        "Batch edit a prefab asset through Unity APIs."
    ),
    command!(
        "open_prefab_stage",
        "prefab",
        OpenPrefabStageArgs,
        "Open a prefab asset in Unity's prefab stage."
    ),
    command!(
        "close_prefab_stage",
        "prefab",
        ClosePrefabStageArgs,
        "Close the active prefab stage."
    ),
    command!(
        "validate_assets",
        "validation",
        ValidateAssetsArgs,
        "Validate a prefab asset or folder recursively."
    ),
];
