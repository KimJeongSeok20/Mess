using System.Collections.Generic;
using System.IO;
using Esper.SkillWeb;
using Esper.SkillWeb.Graph;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Authors the perk skill nodes (names from PlayerPerks.CodeDefaults) into the SkillWeb graph
/// used by the terminal, so the perk system works without hand-placing nodes.
/// Idempotent: existing skills/nodes are reused, only missing ones are created.
/// Menu: Tools/StillWorking/SkillWeb/Build Perk Nodes
/// </summary>
public static class SkillWebPerkAuthoring
{
    private const string TargetWebName = "Survivability Test";
    private const string RootSkillName = "Health Boost I";
    public const string PerkIconDirectory = "Assets/UI/SkillWeb/Icons";

    private static readonly Dictionary<string, string> PerkIconNames = new()
    {
        { "Health Boost I", "health_boost_1" },
        { "Health Boost II", "health_boost_2" },
        { "Iron Lungs", "iron_lungs" },
        { "Second Wind", "second_wind" },
        { "Thick Skin", "thick_skin" },
        { "Fleet Foot I", "fleet_foot_1" },
        { "Fleet Foot II", "fleet_foot_2" },
        { "Sprinter I", "sprinter_1" },
        { "Sprinter II", "sprinter_2" },
        { "Long Jump", "long_jump" },
        { "Double Jump", "double_jump" },
        { "Adrenaline", "adrenaline" },
        { "Ground Slam", "ground_slam" },
        { "Steady Hands I", "steady_hands_1" },
        { "Steady Hands II", "steady_hands_2" },
        { "Free Forge", "free_forge" },
        { "Master Smith", "master_smith" },
        { "Safety Net", "safety_net" },
        { "Second Chance", "second_chance" },
        { "Haggler", "haggler" },
        { "Last Stand", "last_stand" },
        { "Team Insurance", "team_insurance" },
        { "Battery Cell I", "battery_cell_1" },
        { "Battery Cell II", "battery_cell_2" },
        { "Battery Cell III", "battery_cell_3" },
    };

    // Branch layout: each branch chains from the root. (label, skills in order)
    private static readonly (string branch, string[] skills)[] Branches =
    {
        ("body", new[] { "Health Boost II", "Iron Lungs", "Second Wind", "Thick Skin" }),
        ("mobility", new[] { "Fleet Foot I", "Fleet Foot II", "Sprinter I", "Sprinter II", "Long Jump", "Double Jump" }),
        ("combat", new[] { "Adrenaline", "Ground Slam" }),
        ("forge", new[] { "Steady Hands I", "Steady Hands II", "Free Forge", "Master Smith", "Safety Net", "Second Chance" }),
        ("survival", new[] { "Haggler", "Last Stand", "Team Insurance" }),
        ("power", new[] { "Battery Cell I", "Battery Cell II", "Battery Cell III" }),
    };

    // Civic rank (SkillWeb.playerLevel) needed to buy the "special" nodes. Rank rises with every
    // paid tax cycle, so these are the perks the company sells to good citizens.
    private static readonly Dictionary<string, int> LevelRequirements = new()
    {
        { "Battery Cell I", 1 },
        { "Battery Cell II", 2 },
        { "Battery Cell III", 4 },
        { "Haggler", 2 },
        { "Free Forge", 2 },
        { "Safety Net", 3 },
        { "Master Smith", 3 },
        { "Second Chance", 4 },
        { "Team Insurance", 4 },
        { "Last Stand", 3 },
    };

    [MenuItem("Tools/StillWorking/SkillWeb/Build Perk Nodes")]
    public static void BuildFromMenu()
    {
        string report = Build();
        Debug.Log(report);
    }

    [MenuItem("Tools/StillWorking/SkillWeb/Refresh Perk Descriptions")]
    public static void RefreshDescriptionsFromMenu() => Debug.Log(RefreshDescriptions());

    [MenuItem("Tools/StillWorking/SkillWeb/Refresh Perk Icons")]
    public static void RefreshIconsFromMenu() => Debug.Log(RefreshIcons());

    [MenuItem("Tools/StillWorking/SkillWeb/Refresh Perk Prerequisites")]
    public static void RefreshPrerequisitesFromMenu() => Debug.Log(RefreshPrerequisites());

    public static string RefreshPrerequisites()
    {
        WebGraph web = FindWeb(TargetWebName);
        if (web == null)
            throw new System.InvalidOperationException($"Skill web '{TargetWebName}' not found.");

        SkillNode root = FindNodeBySkillName(web, RootSkillName);
        if (root == null)
            throw new System.InvalidOperationException($"Root node '{RootSkillName}' not found.");

        // Resolve existing nodes before changing flags; this command never rebuilds the graph.
        var nodes = new List<SkillNode> { root };
        foreach (var branch in Branches)
        {
            foreach (string skillName in branch.skills)
            {
                SkillNode node = FindNodeBySkillName(web, skillName);
                if (node == null)
                    throw new System.InvalidOperationException($"Perk node '{skillName}' not found.");
                nodes.Add(node);
            }
        }

        Undo.RecordObject(web, "Update perk prerequisites");
        int updated = 0;
        foreach (SkillNode node in nodes)
        {
            if (SetPrerequisites(node, node != root))
                updated++;
        }

        if (updated > 0)
        {
            // Saved SkillWeb nodes must pick up the changed flags while keeping their node GUIDs.
            web.changeGuid = System.Guid.NewGuid().ToString();
            EditorUtility.SetDirty(web);
            AssetDatabase.SaveAssetIfDirty(web);
        }

        return $"[SkillWebPerkAuthoring] prerequisitesUpdated={updated} totalNodes={nodes.Count}";
    }

    public static string RefreshIcons()
    {
        WebGraph web = FindWeb(TargetWebName);
        if (web == null)
            throw new System.InvalidOperationException($"Skill web '{TargetWebName}' not found.");

        // Resolve every image before writing any reference, so a missing import cannot leave a partial set.
        var icons = new Dictionary<Skill, Sprite>();
        foreach (SkillNode node in web.skillNodes)
        {
            if (node == null || node.skill == null)
                continue;
            Sprite icon = LoadPerkIcon(node.skill);
            if (icon == null)
                throw new System.InvalidOperationException($"Missing perk icon for '{node.skill.skillName}'.");
            icons[node.skill] = icon;
        }

        int updated = 0;
        foreach (var entry in icons)
        {
            if (SetPerkIcon(entry.Key, entry.Value))
                updated++;
        }
        return $"[SkillWebPerkAuthoring] iconsUpdated={updated} totalSkills={icons.Count}";
    }

    // Update text in place: node IDs, connections and saved progression are unaffected.
    public static string RefreshDescriptions()
    {
        WebGraph web = FindWeb(TargetWebName);
        if (web == null)
            throw new System.InvalidOperationException($"Skill web '{TargetWebName}' not found.");

        var definitions = PlayerPerks.LoadDefinitions();
        int updated = 0;
        foreach (SkillNode node in web.skillNodes)
        {
            if (node != null && RefreshDescription(node.skill, definitions))
                updated++;
        }

        return $"[SkillWebPerkAuthoring] descriptionsUpdated={updated} totalNodes={web.skillNodes.Count}";
    }

    public static string Build()
    {
        WebGraph web = FindWeb(TargetWebName);
        if (web == null)
            return $"[SkillWebPerkAuthoring] Web '{TargetWebName}' not found.";

        var skillsByName = new Dictionary<string, Skill>();
        foreach (Skill skill in LoadAllSkills())
        {
            if (skill != null && !string.IsNullOrEmpty(skill.skillName) && !skillsByName.ContainsKey(skill.skillName))
                skillsByName.Add(skill.skillName, skill);
        }

        Skill root = skillsByName.TryGetValue(RootSkillName, out Skill r) ? r : null;
        SkillNode rootNode = FindNodeBySkillName(web, RootSkillName);
        if (rootNode == null)
            return $"[SkillWebPerkAuthoring] Root node '{RootSkillName}' not found in web.";

        int createdSkills = 0;
        int createdNodes = 0;
        int createdConnections = 0;
        var definitions = PlayerPerks.LoadDefinitions();
        SetPrerequisites(rootNode, false);
        RefreshDescription(root, definitions);
        SetPerkIcon(root, LoadPerkIcon(root));

        float branchSpacingX = 260f;
        float nodeSpacingY = 150f;
        float startX = rootNode.position.x - branchSpacingX * (Branches.Length - 1) / 2f;

        for (int b = 0; b < Branches.Length; b++)
        {
            string[] chain = Branches[b].skills;
            int previousNodeId = rootNode.id;
            float x = startX + b * branchSpacingX;

            for (int i = 0; i < chain.Length; i++)
            {
                string skillName = chain[i];

                if (!skillsByName.TryGetValue(skillName, out Skill skill) || skill == null)
                {
                    skill = CreateSkill(skillName, root);
                    skillsByName[skillName] = skill;
                    createdSkills++;
                }

                RefreshDescription(skill, definitions);
                SetPerkIcon(skill, LoadPerkIcon(skill));

                int requiredRank = LevelRequirements.TryGetValue(skillName, out int req) ? req : 0;
                if (skill.levelRequirement != requiredRank)
                {
                    skill.levelRequirement = requiredRank;
                    EditorUtility.SetDirty(skill);
                }

                SkillNode node = FindNodeBySkillName(web, skillName);
                if (node == null)
                {
                    int id = web.GetAvailableNodeID();
                    Vector2 position = new Vector2(x, rootNode.position.y + nodeSpacingY * (i + 1));
                    node = new SkillNode(id, skill, position)
                    {
                        hasConnectionDependency = true,
                        dependencyCount = 1,
                        maxedRequirementCount = 0
                    };
                    if (string.IsNullOrEmpty(node.guid))
                        node.guid = System.Guid.NewGuid().ToString();
                    web.skillNodes.Add(node);
                    createdNodes++;
                }

                SetPrerequisites(node, true);

                if (!HasConnection(web, previousNodeId, node.id))
                {
                    web.connections.Add(new Connection(previousNodeId, node.id, 0, 0, 0));
                    createdConnections++;
                }

                previousNodeId = node.id;
            }
        }

        web.changeGuid = System.Guid.NewGuid().ToString();
        EditorUtility.SetDirty(web);
        TrySave(web);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return $"[SkillWebPerkAuthoring] web='{web.webName}' skillsCreated={createdSkills} nodesCreated={createdNodes} connectionsCreated={createdConnections} totalNodes={web.skillNodes.Count}";
    }

    private static int _nextSkillId = -1;

    /// <summary>
    /// Skill.Create() needs SkillWeb.Settings (only loaded by the runtime UI), so build the asset
    /// by hand: next free id, template icons, dataset copied from the template.
    /// </summary>
    private static Skill CreateSkill(string skillName, Skill template)
    {
        string directory = template != null
            ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(template))
            : "Assets/StylishEsper/SkillWeb/Resources/SkillWeb/Skills";
        if (string.IsNullOrEmpty(directory))
            directory = "Assets/StylishEsper/SkillWeb/Resources/SkillWeb/Skills";
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        if (_nextSkillId < 0)
        {
            _nextSkillId = 10001;
            foreach (Skill existing in LoadAllSkills())
                _nextSkillId = Mathf.Max(_nextSkillId, existing.id + 1);
        }

        Skill skill = ScriptableObject.CreateInstance<Skill>();
        skill.id = _nextSkillId++;
        skill.skillName = skillName;
        skill.maxLevel = 1;
        skill.levelRequirement = 0;
        if (template != null)
        {
            skill.tagIndex = template.tagIndex;
            skill.size = template.size;
            skill.lockedIcon = template.lockedIcon;
            skill.unlockedIcon = template.unlockedIcon;
            skill.obtainedIcon = template.obtainedIcon;
            skill.maxedIcon = template.maxedIcon;
        }
        else
        {
            skill.lockedIcon.color = Color.white;
            skill.unlockedIcon.color = Color.white;
            skill.obtainedIcon.color = Color.white;
            skill.maxedIcon.color = Color.white;
        }

        string sanitized = skill.SanitizeName(skillName);
        string assetName = $"{skill.id}_{sanitized}";
        skill.name = assetName;
        string path = Path.Combine(directory, assetName + ".asset").Replace('\\', '/');
        AssetDatabase.CreateAsset(skill, path);

        if (template != null && template.dataset != null)
        {
            skill.dataset = template.GenerateDatasetCopy(skill);
            if (skill.dataset != null)
            {
                skill.dataset.skill = skill;
                EditorUtility.SetDirty(skill.dataset);
            }
        }

        EditorUtility.SetDirty(skill);
        TrySave(skill);
        return skill;
    }

    private static void TrySave(SkillWebObject obj)
    {
        try
        {
            obj.Save();
        }
        catch (System.Exception exception)
        {
            // Database record update can fail when the SQLite database is not initialized; the asset itself is still saved.
            Debug.LogWarning($"[SkillWebPerkAuthoring] Save() threw for {obj.name}: {exception.Message}");
            EditorUtility.SetDirty(obj);
        }
    }

    private static bool SetPrerequisites(SkillNode node, bool requiresPredecessor)
    {
        if (node.hasConnectionDependency == requiresPredecessor &&
            node.dependencyCount == 1 && node.maxedRequirementCount == 0)
            return false;

        node.hasConnectionDependency = requiresPredecessor;
        node.dependencyCount = 1;
        node.maxedRequirementCount = 0;
        return true;
    }

    private static bool RefreshDescription(Skill skill, IReadOnlyList<PlayerPerks.PerkEntry> definitions)
    {
        if (skill == null || skill.dataset is not DefaultSkillDataset dataset)
            return false;

        foreach (var definition in definitions)
        {
            if (definition.skillName != skill.skillName)
                continue;

            if (string.IsNullOrWhiteSpace(definition.description) || dataset.description == definition.description)
                return false;

            Undo.RecordObject(dataset, "Update perk description");
            dataset.description = definition.description;
            EditorUtility.SetDirty(dataset);
            AssetDatabase.SaveAssetIfDirty(dataset);
            return true;
        }

        return false;
    }

    private static Sprite LoadPerkIcon(Skill skill)
    {
        if (skill == null || !PerkIconNames.TryGetValue(skill.skillName, out string name))
            return null;
        return AssetDatabase.LoadAssetAtPath<Sprite>($"{PerkIconDirectory}/{name}.png");
    }

    private static bool SetPerkIcon(Skill skill, Sprite icon)
    {
        if (skill == null || icon == null ||
            (skill.lockedIcon.icon == icon && skill.unlockedIcon.icon == icon &&
             skill.obtainedIcon.icon == icon && skill.maxedIcon.icon == icon))
            return false;

        Undo.RecordObject(skill, "Update perk icon");
        // Preserve the existing locked/available/obtained state colors.
        skill.lockedIcon.icon = icon;
        skill.unlockedIcon.icon = icon;
        skill.obtainedIcon.icon = icon;
        skill.maxedIcon.icon = icon;
        EditorUtility.SetDirty(skill);
        AssetDatabase.SaveAssetIfDirty(skill);
        return true;
    }

    private static WebGraph FindWeb(string webName)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:WebGraph"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            WebGraph web = AssetDatabase.LoadAssetAtPath<WebGraph>(path);
            if (web != null && (string.Equals(web.webName, webName, System.StringComparison.OrdinalIgnoreCase)
                                || Path.GetFileNameWithoutExtension(path).EndsWith(webName.Replace(" ", string.Empty))))
                return web;
        }

        return null;
    }

    private static IEnumerable<Skill> LoadAllSkills()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Skill"))
        {
            Skill skill = AssetDatabase.LoadAssetAtPath<Skill>(AssetDatabase.GUIDToAssetPath(guid));
            if (skill != null)
                yield return skill;
        }
    }

    private static SkillNode FindNodeBySkillName(WebGraph web, string skillName)
    {
        foreach (SkillNode node in web.skillNodes)
        {
            if (node != null && node.skill != null && string.Equals(node.skill.skillName, skillName, System.StringComparison.OrdinalIgnoreCase))
                return node;
        }

        return null;
    }

    private static bool HasConnection(WebGraph web, int outputNodeId, int inputNodeId)
    {
        foreach (Connection connection in web.connections)
        {
            if (connection != null && connection.outputNodeID == outputNodeId && connection.inputNodeID == inputNodeId)
                return true;
        }

        return false;
    }
}
