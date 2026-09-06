using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "CorpseDropTable", menuName = "Game/CorpseDropTable")]
public class CorpseDropTable : ScriptableObject
{
    [System.Serializable]
    public class CorpseTypeDrops
    {
        [Header("시체 타입 키워드")]
        [Tooltip("시체 이름에 이 키워드가 포함되면 해당 드롭 사용")]
        public string corpseKeyword;  // "zombie", "mummy", "skeleton" 등
        
        [Header("드롭 가능 아이템")]
        public List<DropInfo> possibleDrops;
    }
    
    [System.Serializable]
    public class DropInfo
    {
        [Header("아이템 설정")]
        public GameObject itemPrefab;  // Network Prefab으로 등록 필수!
        
        [Header("수량")]
        public int minQuantity = 1;
        public int maxQuantity = 1;
        
        [Header("확률")]
        [Range(0, 100)]
        public float dropChance = 50f;
        
        [Header("가격 범위")]
        public int minPrice = 10;
        public int maxPrice = 100;
    }
    
    [Header("시체 타입별 드롭 설정")]
    [Tooltip("위에서부터 순서대로 체크합니다. Generic은 맨 아래 두세요.")]
    public List<CorpseTypeDrops> corpseDrops = new List<CorpseTypeDrops>()
    {
        // 기본값 예시
        new CorpseTypeDrops() 
        { 
            corpseKeyword = "generic",
            possibleDrops = new List<DropInfo>()
        }
    };
}