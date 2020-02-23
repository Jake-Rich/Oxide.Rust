using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Text;

namespace Oxide.Game.Rust
{
    public static class IPlayerExtension
    {
        public static IPlayer IPlayer(this BasePlayer player)
        {
            return RustCovalenceProvider.Instance.PlayerManager.FindPlayerById( player.UserIDString );
        }
    }
}
