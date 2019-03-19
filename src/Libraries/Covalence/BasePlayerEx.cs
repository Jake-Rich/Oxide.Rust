using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oxide.Rust.Libraries.Covalence
{
    public static class BasePlayerEx
    {
        public static IPlayer IPlayer(this BasePlayer player)
        {
            return RustCovalenceProvider.Instance.PlayerManager.FindPlayerById(player.UserIDString);
        }
    }
}
