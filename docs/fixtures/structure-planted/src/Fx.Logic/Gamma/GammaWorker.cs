using Fx.Data;
namespace Fx.Logic
{
    public class GammaWorker
    {
        private readonly AlphaWorker alpha = new AlphaWorker();
        private readonly RepoDal repo = new RepoDal();
        public void Run(int id) { alpha.Run(id); repo.Save(id); }
    }
}
