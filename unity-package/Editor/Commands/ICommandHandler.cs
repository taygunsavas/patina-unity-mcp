using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace Patina.Editor.Commands
{
    public interface ICommandHandler
    {
        Task<object> HandleAsync(JObject parameters);
    }
}
