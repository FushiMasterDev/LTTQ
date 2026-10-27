using Homestay.BLL.Infrastructure;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
namespace Homestay.BLL.Tests;

internal sealed class M41TestClient
{
    public M21TestClient Base { get; } = new();
    public OwnerService Owners { get; }
    public HomestayService Homes { get; }
    public RoomService Rooms { get; }
    public ContractService Contracts { get; }
    public FileService Files { get; }
    public M41TestClient()
    {
        var auth = new Authorizer(Base.Current); var scope = new DataScope(Base.Current); var clock = new DbClock(Base.Factory);
        Owners = new(auth, scope, Base.Factory, new OwnerRepository()); Homes = new(auth, Base.Current, scope, Base.Factory, new HomestayRepository(), clock);
        Rooms = new(auth, scope, Base.Factory, new RoomRepository(), clock, Base.Availability); Contracts = new(auth, scope, Base.Factory, new ContractRepository());
        Files = new(auth, Base.Factory, new EvidenceRepository(), scope);
    }
}
