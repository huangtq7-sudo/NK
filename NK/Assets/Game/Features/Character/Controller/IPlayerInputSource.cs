using Naraka.Features.Character.Model;

namespace Naraka.Features.Character.Controller
{
    /// <summary>
    /// 玩家输入意图的来源。具体的 Input System Action Map、键位与重映射
    /// 全部留在 Infrastructure，业务层只拿到一帧纯数据。
    /// </summary>
    public interface IPlayerInputSource
    {
        /// <summary>采样本帧输入。输入被禁用时必须返回空输入而不是上一帧的残留值。</summary>
        PlayerInputFrame Sample();

        /// <summary>
        /// 启用或禁用 Player Action Map。加载、出场、死亡与设置界面打开时必须禁用。
        /// </summary>
        void SetPlayerInputEnabled(bool enabled);

        bool IsPlayerInputEnabled { get; }
    }
}
