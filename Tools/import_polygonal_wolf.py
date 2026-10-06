# -*- coding: utf-8 -*-
"""
从 Polygonal Creatures Pack 的 .unitypackage 里**只**提取暮影妖狼需要的资源。

为什么不用 Unity 的 Custom Package 导入界面：那是一次手工勾选，没有可复查的记录，
也无法在以后证明"当时到底导了哪些文件"。这个脚本是声明式的：白名单写在代码里，
任何一条没在白名单里的条目都不会落盘，运行后打印逐条清单与 SHA-256。

硬约束（对应本轮任务书第四节）：

- 只处理 `Assets/Polygonal Creatures Pack/Polygonal Wolf/` 下的白名单条目；
- 同时提取 `asset` 与 `asset.meta`，**原样写出 meta**，因此第三方 GUID 不变；
- 保持第三方原始目录，不把 FBX/材质/贴图搬进 `Assets/Game/`；
- 不导入任何演示场景、演示 Animator、其他九类怪物或 Brown/White 变体；
- unitypackage 本身不进仓库。

用法（仓库根目录）：

    python Tools/import_polygonal_wolf.py            # 预演，只打印不写盘
    python Tools/import_polygonal_wolf.py --apply    # 真正写盘
"""
import argparse
import hashlib
import io
import os
import sys
import tarfile

PACKAGE = (r'E:\素材\30 Unity Asset Polygonal - Creatures Pack v1.0'
           r'\Unity Asset Polygonal - Creatures Pack v1.0.unitypackage')

# 已审计的包身份。对不上就停：换了一份包就必须重新审计，不能默默导入别的东西。
EXPECTED_BYTES = 38110997
EXPECTED_SHA256 = '70b6b6a423bac0080227f816c19c94b3133eb90b90b8b860528c7284a0412f18'

UNITY_PROJECT = 'NK'
WOLF = 'Assets/Polygonal Creatures Pack/Polygonal Wolf/'

# 白名单：本轮暮影妖狼真正需要的条目。
#
# 动画只取 `WO Root`（without root motion）那一套：移动由 NavMeshAgent 与 View 驱动，
# 带 Root 的版本会让动画再推一次世界坐标，造成双倍位移。
ALLOW = [
    # 模型本体与共享基础网格
    WOLF + 'FBX/Polygonal Wolf.FBX',
    WOLF + 'FBX/Base.FBX',

    # 本轮七个状态需要的动画
    WOLF + 'FBX/Polygonal Wolf@Idle.FBX',
    WOLF + 'FBX/Polygonal Wolf@Walk Forward WO Root.FBX',
    WOLF + 'FBX/Polygonal Wolf@Run Forward WO Root.FBX',
    WOLF + 'FBX/Polygonal Wolf@Bite Attack.FBX',
    WOLF + 'FBX/Polygonal Wolf@Breath Attack.FBX',
    WOLF + 'FBX/Polygonal Wolf@Take Damage.FBX',
    WOLF + 'FBX/Polygonal Wolf@Die.FBX',

    # Black 外观（暮影妖狼的默认外观）
    #
    # 刻意**不导入** `Prefabs/Polygonal Wolf Black.prefab`：实测它除了 FBX 之外只依赖
    # 两样东西 —— 内建 Standard 材质（URP 下显示为粉色，必须换成项目自有的 URP 材质）
    # 与 `Animators/Polygonal Wolf.controller`（演示控制器，已排除）。
    # 导入它只会留下一个必然缺失的 Controller 引用，而它能提供的东西
    # （53 节点骨架 + 单个 SkinnedMeshRenderer + Avatar）直接实例化 FBX 就有，
    # 并且它自己也没有任何碰撞体，因此不构成"项目自有碰撞体"的来源。
    WOLF + 'Materials/Polygonal Wolf Black.mat',
    WOLF + 'Textures/Polygonal Wolf Black.png',
    WOLF + 'Textures/Polygonal Wolf Black Glow.png',
]

# 明确排除（写出来是为了让"没导入"这件事可复查，不是注释性说明）。
EXCLUDE_REASONS = [
    ('其他九类怪物目录', 'Dragon / Giant Bee / Golem / King Cobra / Magma / '
                         'One Eyed Bat / Spiderling Venom / Treant / Treasure Chest（P4）'),
    ('演示场景', 'Scenes/Demo Scene.unity、Scenes/Turntable Scene.unity'),
    ('演示 Animator', 'Animators/ 下三个 controller：演示用，本项目自建单向投影控制器'),
    ('演示工具与后处理', 'FBX/Rotate.anim、PPP/Post Processing.asset、Materials/Demo Ground.mat'),
    ('Brown / White 变体', '暮影妖狼默认采用 Black 外观，这两套没有被 Black 资源引用'),
    ('带 Root 的位移动画', '@Walk/@Run/@Jump/@Pound Attack 的 W Root 版本：'
                           '移动由 NavMeshAgent 驱动，Root Motion 必须关闭'),
    ('本轮不用的动画', '@Walk Backward、@Jump、@Pound Attack、@Howl、@Eating、'
                       '@Resting、@Look Around（七个状态之外）'),
    ('包内说明文本', 'Read me.txt、www.meshtint.com.txt（保留在原包，不进仓库）'),
    ('Black 外观 Prefab', 'Prefabs/Polygonal Wolf Black.prefab：它引用已排除的演示 Controller，'
                          '且本项目自建 DuskshadowWolf.prefab，详见上面白名单处的说明'),
]


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def read_entries(package):
    """guid -> {pathname, asset bytes, meta bytes}。只读整包一次。"""
    entries = {}
    with tarfile.open(package, 'r:gz') as tf:
        for member in tf:
            if not member.isfile():
                continue
            parts = member.name.split('/')
            if len(parts) < 2:
                continue
            guid, leaf = parts[0], parts[-1]
            entry = entries.setdefault(guid, {'pathname': None, 'asset': None, 'meta': None})
            if leaf == 'pathname':
                raw = tf.extractfile(member).read().decode('utf-8')
                # pathname 文件常带第二行（资产来源标记），只取第一行。
                entry['pathname'] = raw.replace('\r', '').split('\n')[0].strip()
            elif leaf == 'asset':
                entry['asset'] = tf.extractfile(member).read()
            elif leaf == 'asset.meta':
                entry['meta'] = tf.extractfile(member).read()
    return entries


def guid_in(meta_bytes):
    if not meta_bytes:
        return None
    for line in meta_bytes.decode('utf-8', 'replace').splitlines():
        if line.startswith('guid:'):
            return line.split(':', 1)[1].strip()
    return None


def ancestors(pathname):
    """`Assets/a/b/c.fbx` → ['Assets/a', 'Assets/a/b']（不含 `Assets` 自身）。"""
    segments = pathname.split('/')[:-1]
    out = []
    for i in range(2, len(segments) + 1):
        out.append('/'.join(segments[:i]))
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--apply', action='store_true', help='真正写盘；缺省只预演')
    parser.add_argument('--package', default=PACKAGE)
    parser.add_argument('--project', default=UNITY_PROJECT)
    args = parser.parse_args()

    if not os.path.isfile(args.package):
        print('找不到素材包：%s' % args.package)
        return 2

    size = os.path.getsize(args.package)
    digest = sha256_of(args.package)
    print('素材包      : %s' % args.package)
    print('字节数      : %d %s' % (size, '✓' if size == EXPECTED_BYTES else '✗ 与审计值不一致'))
    print('SHA-256     : %s %s' % (digest, '✓' if digest == EXPECTED_SHA256 else '✗ 与审计值不一致'))
    if size != EXPECTED_BYTES or digest != EXPECTED_SHA256:
        print('\n包身份与已审计值不一致，拒绝导入。请先重新审计包内容。')
        return 3

    entries = read_entries(args.package)
    by_path = {}
    for guid, entry in entries.items():
        if entry['pathname']:
            by_path[entry['pathname']] = (guid, entry)

    # 白名单必须条条都在包里，少一条就停：静默少导一个文件会变成运行期的 Missing 引用。
    missing = [p for p in ALLOW if p not in by_path]
    if missing:
        print('\n白名单里有条目不在包内：')
        for p in missing:
            print('  %s' % p)
        return 4

    # 需要的目录条目（为了保留第三方原始目录的 GUID）。
    folders = []
    for p in ALLOW:
        for folder in ancestors(p):
            if folder not in folders:
                folders.append(folder)
    folders.sort()

    planned = []
    for folder in folders:
        hit = by_path.get(folder)
        planned.append(('目录', folder, None, hit[1]['meta'] if hit else None,
                        guid_in(hit[1]['meta']) if hit else '(Unity 自动生成)'))
    for p in ALLOW:
        _, entry = by_path[p]
        planned.append(('文件', p, entry['asset'], entry['meta'], guid_in(entry['meta'])))

    print('\n=== 计划写出 %d 个目录 + %d 个文件（各自连同 .meta）===' % (len(folders), len(ALLOW)))
    print('  %-6s %-62s %10s  %s' % ('类型', '工程内路径', '字节', '原始 GUID'))
    print('  ' + '-' * 108)
    written = 0
    for kind, pathname, asset, meta, guid in planned:
        target = os.path.join(args.project, pathname.replace('/', os.sep))
        print('  %-6s %-62s %10s  %s' % (
            kind,
            pathname[len('Assets/Polygonal Creatures Pack/'):] or pathname,
            '-' if asset is None else len(asset),
            guid))

        if not args.apply:
            continue

        if kind == '目录':
            os.makedirs(target, exist_ok=True)
        else:
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with open(target, 'wb') as fh:
                fh.write(asset)
            written += 1

        if meta:
            # 原样写出：meta 里的 guid 必须保持第三方原值。
            with open(target + '.meta', 'wb') as fh:
                fh.write(meta)

    print('\n=== 明确排除 ===')
    for title, detail in EXCLUDE_REASONS:
        print('  %-16s %s' % (title, detail))

    if not args.apply:
        print('\n预演结束，未写入任何文件。加 --apply 才会落盘。')
    else:
        print('\n已写出 %d 个资产文件及其 .meta；目录 meta 同步写出。' % written)
        print('unitypackage 本身不进仓库。')
    return 0


if __name__ == '__main__':
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
    sys.exit(main())
