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
}
