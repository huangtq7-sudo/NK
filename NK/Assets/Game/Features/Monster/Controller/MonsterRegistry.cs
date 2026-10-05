using System;
using System.Collections.Generic;

namespace Naraka.Features.Monster.Controller
{
    /// <summary>
    /// 场上怪物的登记表，外加"当前被关注的那一只"。
    ///
    /// 战斗 HUD 要显示一个目标的血条，但它不该去场景里找怪物，也不该认识 View。
    /// 怪物自己在交战时把焦点指向自己，HUD 只订阅焦点变化。
    /// 这里不保存任何战斗规则：血量、阶段、预警都在各自的 Controller 里。
    /// </summary>
    public interface IMonsterRegistry
    {
        void Register(IMonsterController monster);

        void Unregister(IMonsterController monster);

        /// <summary>把焦点指向这一只。通常在它进入战斗或被打到时调用。</summary>
        void Focus(IMonsterController monster);

        /// <summary>当前焦点。没有焦点时为 null。</summary>
        IMonsterController Focused { get; }

        /// <summary>焦点发生变化。参数可能为 null，表示没有目标了。</summary>
        event Action<IMonsterController> FocusChanged;

        int Count { get; }
    }

    /// <inheritdoc />
    public sealed class MonsterRegistry : IMonsterRegistry
    {
        private readonly List<IMonsterController> _monsters = new List<IMonsterController>(8);

        public IMonsterController Focused { get; private set; }

        public event Action<IMonsterController> FocusChanged;

        public int Count => _monsters.Count;

        public void Register(IMonsterController monster)
        {
            if (monster == null || _monsters.Contains(monster))
            {
                return;
            }

            _monsters.Add(monster);
        }

        public void Unregister(IMonsterController monster)
        {
            if (monster == null)
            {
                return;
            }

            _monsters.Remove(monster);
            if (Focused != monster)
            {
                return;
            }

            // 焦点怪物消失时把焦点交给还活着的另一只；没有就清空，HUD 会隐藏血条。
            Focused = FindLiving();
            FocusChanged?.Invoke(Focused);
        }

        public void Focus(IMonsterController monster)
        {
            if (monster == null || Focused == monster)
            {
                return;
            }

            Register(monster);
            Focused = monster;
            FocusChanged?.Invoke(Focused);
        }

        private IMonsterController FindLiving()
        {
            for (var i = 0; i < _monsters.Count; i++)
            {
                var candidate = _monsters[i];
                if (candidate != null && !candidate.IsDisposed && !candidate.IsDead)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
