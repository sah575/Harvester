namespace Harvester.Race
{
    // Mermi/efekt kendi durumunu burada sıfırlar. Ağ objesi yaşam döngüsü bu havuzun işi değildir.
    public interface IPoolable
    {
        void OnPoolSpawn();
        void OnPoolRelease();
    }
}
