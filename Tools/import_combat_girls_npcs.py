# -*- coding: utf-8 -*-
"""
从 CombatGirls Katana / Sword 两个 .unitypackage 里**只**提取 P4.1 两名 Idle NPC 需要的资源。

做法沿用 `Tools/import_polygonal_wolf.py`：白名单写在代码里，包身份按字节数与 SHA-256 核对，
资产与 `.meta` 原样写出，第三方 GUID 不变；任何不在白名单里的条目都不会落盘。

为什么不用包里的 Prefab / 材质 / Animator（只读审计结论）：
- 演示 Prefab 挂着 Magica Cloth 2 的组件（脚本 GUID 不在包内，工程也没有该插件），导入只会得到 Missing Script；
- 包内材质全部是内建 Standard 着色器加卡通着色器专有属性，URP 下显示为粉色，本项目另建 URP 材质；
- 演示 Animator 引用二三十个战斗动画，NPC 只需要 Idle，本项目另建单状态控制器。

为什么要带上 `Humanoid_Bot/Models` 下两个 FBX：角色模型与 Idle 片段的 Humanoid Avatar 都是
"Copy From Other Avatar"，来源就是这两个文件。缺了它们 Avatar 无法生成，Idle 无法重定向。
它们只作为 Avatar 来源，不在场景中实例化，它们引用的演示材质同样不导入。

两个包里有 27 个同路径但 GUID 不同的目录：先处理的包占用目录 GUID，后者跳过；
同路径文件必须逐字节相同，否则拒绝导入。

用法（仓库根目录）：

    python Tools/import_combat_girls_npcs.py            # 预演，只打印不写盘
    python Tools/import_combat_girls_npcs.py --apply    # 真正写盘
"""
import argparse
import hashlib
import io
import os
import sys
import tarfile

ROOT = 'Assets/CombatGirlsCharacterPack/'
KATANA = ROOT + 'School_Katana_Girl/'
SWORD = ROOT + 'CombatGirl_Sword/'
BOT = ROOT + 'Humanoid_Bot/Models/'

PACKAGES = [
    {
        'name': 'Katana',
        'path': r'E:\素材\CombatGirlsKatanaCharacterPack 1.1.unitypackage',
        'bytes': 96016910,
        'sha256': '80f106cda95038e880234e2eaabc273407a7ce7ac56248987d82c299e3d662f3',
        'allow': [
            KATANA + 'Models/School_Katana_FullBody.fbx',
            KATANA + 'Animations/Normal/Idle.fbx',
            KATANA + 'Texture/Body/Body.png',
            KATANA + 'Texture/Body/Eye.png',
            KATANA + 'Texture/Body/Face.png',
            KATANA + 'Texture/Cloth/Katana_Cloth_01.png',
            KATANA + 'Texture/Cloth/Katana_Glass.png',
            KATANA + 'Texture/Hair/Katana_Hair_01.png',
            KATANA + 'Texture/Sportswear/Sportswear.png',
            KATANA + 'Texture/Weapon/Katana_01.png',
            BOT + 'Humanoid_F.fbx',
        ],
    },
    {
        'name': 'Sword',
        'path': r'E:\素材\CombatGirlsSwordCharacterPack 1.1.unitypackage',
        'bytes': 125490872,
        'sha256': 'a01f9590015432ba3c0ef185f2e0a7bb3899324237a5e0b92f39a0ef0dec1e4a',
        'allow': [
            SWORD + 'Models/CombatGirl_Sword.fbx',
            SWORD + 'Animations/Normal/S_Idle.fbx',
            SWORD + 'Texture/Body/Body.png',
            SWORD + 'Texture/Body/Eye.png',
            SWORD + 'Texture/Body/Face.png',
            SWORD + 'Texture/Cloth/Sword_Cloth.png',
            SWORD + 'Texture/Hair/Sword_Hair.png',
            SWORD + 'Texture/Squire_Cloth/Squire_Cloth.png',
            SWORD + 'Texture/Weapon/Weapon_Sword.png',
            BOT + 'Humanoid_F.fbx',
            BOT + 'Humanoid_Fe_Sword.fbx',
        ],
    },
]

EXCLUDE_REASONS = [
    ('Scripts/、Biperworks_Tools/', '包内运行时脚本与编辑器工具，NPC 只需要模型与 Idle'),
    ('UI_&_environmental/', '演示 UI、展台、穹顶，以及一份与工程 TMP 重复的 TMP_SDF-Mobile 着色器'),
    ('演示 Prefab', '挂 Magica Cloth 2 组件（工程没有该插件），导入只会是 Missing Script'),
    ('包内材质 .mat', '内建 Standard + 卡通着色器属性，URP 下为粉色；本项目另建 URP Lit 材质'),
    ('Animator Controller', '演示控制器引用全部战斗动画；本项目另建只有 Idle 的单状态控制器'),
    ('其他动画', '攻击、移动、受击、死亡、Special 等：NPC 只播放 Idle'),
    ('部件 FBX 与其他配色', 'Parts/ 下拆分部件、Face02-05、Cloth/Hair/Weapon 其余配色贴图'),
    ('Humanoid_Bot 材质与贴图', '两个 Humanoid FBX 只作为 Avatar 来源，不渲染'),
]


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def read_entries(package):
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
                entry['pathname'] = raw.replace('\r', '').split('\n')[0].strip()
            elif leaf == 'asset':
                entry['asset'] = tf.extractfile(member).read()
            elif leaf == 'asset.meta':
                entry['meta'] = tf.extractfile(member).read()
    by_path = {}
    for entry in entries.values():
        if entry['pathname']:
            by_path[entry['pathname']] = entry
    return by_path


def guid_in(meta_bytes):
    if not meta_bytes:
        return None
    for line in meta_bytes.decode('utf-8', 'replace').splitlines():
        if line.startswith('guid:'):
            return line.split(':', 1)[1].strip()
    return None


def ancestors(pathname):
    segments = pathname.split('/')[:-1]
    return ['/'.join(segments[:i]) for i in range(2, len(segments) + 1)]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--apply', action='store_true', help='真正写盘；缺省只预演')
    parser.add_argument('--project', default='NK')
    args = parser.parse_args()

    planned_files = {}
    planned_folders = {}
    for package in PACKAGES:
        path = package['path']
        if not os.path.isfile(path):
            print('找不到素材包：%s' % path)
            return 2

        size = os.path.getsize(path)
        digest = sha256_file(path)
        ok = size == package['bytes'] and digest == package['sha256']
        print('%-7s %s\n        %d 字节  SHA-256 %s  %s' % (
            package['name'], path, size, digest, '✓' if ok else '✗ 与审计值不一致'))
        if not ok:
            print('\n包身份与已审计值不一致，拒绝导入。请先重新审计包内容。')
            return 3

        by_path = read_entries(path)
        missing = [p for p in package['allow'] if p not in by_path]
        if missing:
            print('\n白名单里有条目不在 %s 包内：' % package['name'])
            for p in missing:
                print('  %s' % p)
            return 4

        for p in package['allow']:
            entry = by_path[p]
            if p in planned_files:
                previous = planned_files[p]
                if sha256_bytes(previous['asset']) != sha256_bytes(entry['asset']) or \
                        guid_in(previous['meta']) != guid_in(entry['meta']):
                    print('\n两个包里的同路径文件内容或 GUID 不同，拒绝导入：%s' % p)
                    return 5
                continue

            planned_files[p] = entry
            for folder in ancestors(p):
                if folder not in planned_folders:
                    hit = by_path.get(folder)
                    planned_folders[folder] = hit['meta'] if hit else None

    print('\n=== 计划写出 %d 个目录 + %d 个文件（各自连同 .meta）===' % (
        len(planned_folders), len(planned_files)))
    written = 0
    total_bytes = 0
    for folder in sorted(planned_folders):
        meta = planned_folders[folder]
        print('  目录  %-80s %s' % (folder[len(ROOT):] or folder, guid_in(meta) or '(Unity 自动生成)'))
        if args.apply:
            target = os.path.join(args.project, folder.replace('/', os.sep))
            os.makedirs(target, exist_ok=True)
            if meta:
                with open(target + '.meta', 'wb') as fh:
                    fh.write(meta)

    for pathname in sorted(planned_files):
        entry = planned_files[pathname]
        total_bytes += len(entry['asset'])
        print('  文件  %-80s %10d  %s  sha256=%s' % (
            pathname[len(ROOT):], len(entry['asset']), guid_in(entry['meta']), sha256_bytes(entry['asset'])[:16]))
        if args.apply:
            target = os.path.join(args.project, pathname.replace('/', os.sep))
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with open(target, 'wb') as fh:
                fh.write(entry['asset'])
            with open(target + '.meta', 'wb') as fh:
                fh.write(entry['meta'])
            written += 1

    print('\n合计 %.1f MiB' % (total_bytes / 1048576.0))
    print('\n=== 明确排除 ===')
    for title, detail in EXCLUDE_REASONS:
        print('  %-28s %s' % (title, detail))

    if not args.apply:
        print('\n预演结束，未写入任何文件。加 --apply 才会落盘。')
    else:
        print('\n已写出 %d 个资产文件及其 .meta。unitypackage 本身不进仓库。' % written)
    return 0


if __name__ == '__main__':
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
    sys.exit(main())
