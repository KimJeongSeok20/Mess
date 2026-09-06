using UnityEngine;

/// <summary>
/// 포장된 아이템 - 선물박스로 변환된 아이템 (GameObject 프리팹)
/// Item을 상속받아 인벤토리 시스템과 호환. 가격은 서버가 스폰할 때 Item.price SyncVar로 설정된다
/// (NetworkPlayer.RequestDropReceiptServerRpc). 로컬 Instantiate + ServerRpc 초기화 경로는 제거했다.
/// </summary>
public class GiftBoxItem : Item
{
    /// <summary>포장 전 원본 아이템 이름 (서버 로컬 정보, 표시용).</summary>
    public string OriginalItemName { get; private set; } = string.Empty;

    /// <summary>서버에서만 호출. 스폰 직후 원본 이름을 기록한다.</summary>
    public void SetOriginalItemNameOnServer(string originalName)
    {
        OriginalItemName = originalName ?? string.Empty;
    }

    [ContextMenu("Print Info")]
    private void PrintInfo()
    {
        Debug.Log($"[GiftBoxItem] Original: {OriginalItemName}, Item Price: {Price}");
    }
}
