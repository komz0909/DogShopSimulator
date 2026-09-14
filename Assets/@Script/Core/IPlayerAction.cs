namespace DogShop.Core
{
    /// <summary>
    /// 플레이어 행동. 게임시간은 소모하지 않는다 — 재화·재고·훈련 슬롯을 소모한다.
    /// 비용 지불은 각 액션이 Execute에서 직접 처리하고, 실행 여부 판정은 CanExecute가 이유와 함께 돌려준다.
    /// </summary>
    public interface IPlayerAction
    {
        bool CanExecute(out string reason);
        void Execute();
    }
}
