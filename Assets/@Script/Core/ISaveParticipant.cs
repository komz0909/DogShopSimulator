namespace DogShop.Core
{
    /// <summary>
    /// 세이브에 자기 상태를 실어 보내는 매니저. SaveManager가 @Managers 하위에서 전부 모아
    /// 순서대로 호출하므로, 새 매니저를 추가할 때 SaveManager를 고치지 않아도 된다.
    /// </summary>
    public interface ISaveParticipant
    {
        void CaptureInto(SaveData data);
        void RestoreFrom(SaveData data);
    }

    /// <summary>
    /// 불러올 때 <b>먼저</b> 돌아야 하는 매니저가 단다. 작을수록 먼저, 안 달면 0(계층 순서 그대로).
    /// 계층 순서에 기대면 재고(InventoryManager)가 산 가구(FurnitureShop)보다 먼저 복원돼,
    /// 아직 세워지지 않은 산 진열대의 물건이 통째로 사라졌다.
    /// </summary>
    public interface ISaveRestoreOrder
    {
        int RestoreOrder { get; }
    }
}
