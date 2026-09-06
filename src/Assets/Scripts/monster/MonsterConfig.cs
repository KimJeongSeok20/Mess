using UnityEngine;

[CreateAssetMenu(fileName = "MonsterConfig", menuName = "Dungeon/Monster Config")]
public class MonsterConfig : ScriptableObject
{
    [Header("Base Stats")]
    public string monsterName = "Monster";
    public int maxHealth = 100;

    [Header("Animation Parameters")]
    public string moveSpeedParam = "MoveSpeed";
    public string attackTrigger = "Attack";
    public string hitTrigger = "Hit";
    public string dieTrigger = "Die";

    [Header("Loot")]
    public CorpseDropProfile dropProfile;
    public CorpseDropTable dropTable;
    public string dropTableKeyword;

    [Header("Hit Feedback")]
    public Color hitFlashColor = Color.white;
    public float flashDuration = 0.1f;
    public float knockbackForce = 2f;
}
