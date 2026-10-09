using Naraka.Core.Application.MVC;
using Naraka.Features.Expedition.Model;

namespace Naraka.Features.Expedition.Controller
{
    /// <summary>
    /// P3临时背包与结算摘要的只读状态。当前没有强制绑定任何UI；后续用户制作界面时只订阅本状态。
    /// </summary>
    public readonly struct ExpeditionPresentationState : IPresentationState
    {
        public ExpeditionPresentationState(
            bool isBusy,
            bool hasActive,
            ExpeditionSnapshot active,
            ExpeditionDeathSummary lastDeath,
            ExpeditionSettlementSummary lastSettlement,
            ExpeditionOperationStatus lastStatus,
            string statusMessage)
        {
            IsBusy = isBusy;
            HasActive = hasActive && active != null;
            Active = active;
            LastDeath = lastDeath;
            LastSettlement = lastSettlement;
            LastStatus = lastStatus;
            StatusMessage = statusMessage ?? string.Empty;
        }

        public bool IsBusy { get; }

        public bool HasActive { get; }

        public ExpeditionSnapshot Active { get; }

        public ExpeditionDeathSummary LastDeath { get; }

        public ExpeditionSettlementSummary LastSettlement { get; }

        public ExpeditionOperationStatus LastStatus { get; }

        public string StatusMessage { get; }

        public static ExpeditionPresentationState Initial => new ExpeditionPresentationState(
            false, false, null, null, null, ExpeditionOperationStatus.Success, string.Empty);
    }

    public static class ExpeditionOperationMessages
    {
        public static string Describe(ExpeditionOperationStatus status)
        {
            switch (status)
            {
                case ExpeditionOperationStatus.Success:
                    return string.Empty;
                case ExpeditionOperationStatus.Unauthenticated:
                    return "登录状态已失效，请重新登录。";
                case ExpeditionOperationStatus.InvalidRequest:
                    return "远征请求无效，请重试。";
                case ExpeditionOperationStatus.NotFound:
                    return "没有可继续的远征。";
                case ExpeditionOperationStatus.DatabaseUnavailable:
                    return "远征数据暂时不可用，请稍后重试。";
                case ExpeditionOperationStatus.Conflict:
                    return "已有远征正在进行。";
                case ExpeditionOperationStatus.TransportFailure:
                    return "无法连接服务器，请检查网络后重试。";
                case ExpeditionOperationStatus.ServerCapabilityMissing:
                    return "服务器尚未部署远征功能。";
                default:
                    return "远征操作失败，请稍后重试。";
            }
        }
    }
}
