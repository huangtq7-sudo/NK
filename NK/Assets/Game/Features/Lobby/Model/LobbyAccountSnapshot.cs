namespace Naraka.Features.Lobby.Model
{
    /// <summary>
    /// 服务端账号概要的不可变快照。只能通过 TryCreate 构造，
    /// 因此非法等级或负数余额无法进入业务层，也就无法显示到界面上。
    /// </summary>
    public readonly struct LobbyAccountSnapshot
    {
        public const int MinimumAccountLevel = 1;

        private LobbyAccountSnapshot(int accountLevel, long copper, long silk, long gold)
        {
            AccountLevel = accountLevel;
            Copper = copper;
            Silk = silk;
            Gold = gold;
        }

        public int AccountLevel { get; }

        public long Copper { get; }

        public long Silk { get; }

        public long Gold { get; }

        public static bool TryCreate(
            int accountLevel,
            long copper,
            long silk,
            long gold,
            out LobbyAccountSnapshot snapshot)
        {
            if (accountLevel < MinimumAccountLevel || copper < 0L || silk < 0L || gold < 0L)
            {
                snapshot = default;
                return false;
            }

            snapshot = new LobbyAccountSnapshot(accountLevel, copper, silk, gold);
            return true;
        }
    }
}
