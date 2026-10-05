using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Naraka.Features.Monster.View
{
    /// <summary>
    /// 怪物生成点。
    ///
    /// 本阶段只需要一只灰盒狼，但接口按"会有很多只"来写：
    /// 上限、补充间隔与预留的回收入口都在这里，因此后续接对象池时
    /// 不需要把架构从"场景里写死一只"重构回来。
    ///
    /// 刷新规则（每 20 秒补 1 只、上限 3 只）属于 P3 远征内容，本阶段不实现：
    /// <see cref="maxAlive"/> 默认 1，<see cref="respawnSeconds"/> 默认 0（不补充）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterSpawner : MonoBehaviour
    {
        [Tooltip("要生成的怪物 Prefab。本阶段指向 GrayboxWolf。")]
        [SerializeField] private GameObject monsterPrefab;

        [Tooltip("同时存活上限。本阶段为 1。")]
        [SerializeField] private int maxAlive = 1;

        [Tooltip("补充间隔（秒）。0 表示不补充，死了就没了。")]
        [SerializeField] private float respawnSeconds;

        [Tooltip("生成位置相对本对象的偏移。")]
        [SerializeField] private Vector3 spawnOffset = Vector3.zero;

        private readonly List<GameObject> _alive = new List<GameObject>(4);
        private IObjectResolver _resolver;
        private float _respawnRemaining;

        /// <summary>当前存活数量。PlayMode 测试用它断言"只生成一只"。</summary>
        public int AliveCount
        {
            get
            {
                Prune();
                return _alive.Count;
            }
        }

        /// <summary>本生成点累计生成过多少只。用来证明没有重复生成。</summary>
        public int SpawnCount { get; private set; }

        [Inject]
        public void Construct(IObjectResolver resolver) => _resolver = resolver;

        private void Start()
        {
            if (monsterPrefab == null)
            {
                Debug.LogError("MonsterSpawner 未绑定怪物 Prefab，不会生成任何怪物。", this);
                return;
            }

            FillToCapacity();
        }

        private void Update()
        {
            if (respawnSeconds <= 0f || monsterPrefab == null)
            {
                return;
            }

            Prune();
            if (_alive.Count >= maxAlive)
            {
                return;
            }

            _respawnRemaining -= Time.deltaTime;
            if (_respawnRemaining > 0f)
            {
                return;
            }

            _respawnRemaining = respawnSeconds;
            Spawn();
        }

        private void FillToCapacity()
        {
            Prune();
            while (_alive.Count < maxAlive)
            {
                if (!Spawn())
                {
                    return;
                }
            }
        }

        private bool Spawn()
        {
            var position = transform.TransformPoint(spawnOffset);
            var instance = _resolver != null
                ? _resolver.Instantiate(monsterPrefab, position, transform.rotation)
                : Instantiate(monsterPrefab, position, transform.rotation);
            if (instance == null)
            {
                return false;
            }

            instance.name = $"{monsterPrefab.name}_{SpawnCount + 1}";
            _alive.Add(instance);
            SpawnCount++;
            return true;
        }

        /// <summary>清掉已经被销毁的条目。不使用 LINQ，倒序删除避免移动元素。</summary>
        private void Prune()
        {
            for (var i = _alive.Count - 1; i >= 0; i--)
            {
                if (_alive[i] == null)
                {
                    _alive.RemoveAt(i);
                }
            }
        }
    }
}
