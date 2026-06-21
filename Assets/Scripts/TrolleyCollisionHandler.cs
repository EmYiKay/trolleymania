using UnityEngine;

/// <summary>
/// Script ini ditempelkan pada GameObject induk Trolley (yang memiliki Rigidbody dan TrolleyController).
/// Berfungsi sebagai pusat penangan tabrakan fisik (OnCollisionEnter) untuk mengurangi HP Player 
/// atau Durabilitas Trolley berdasarkan kondisi kecepatan tabrakan dan tipe objek yang ditabrak.
/// </summary>
[RequireComponent(typeof(TrolleyController))]
public class TrolleyCollisionHandler : MonoBehaviour
{
    [Header("NPC Speed Settings")]
    [Tooltip("Kecepatan maksimum dari Trolley NPC. Digunakan untuk menghitung rasio kecepatan NPC.")]
    [SerializeField] private float npcMaxSpeed = 8f;

    [Header("Damage Settings")]
    [Tooltip("Jumlah pengurangan durabilitas trolley saat menabrak dinding pada kecepatan tinggi.")]
    [SerializeField] private int wallCollisionDamage = 1;

    [Tooltip("Jumlah pengurangan durabilitas trolley saat ditabrak Trolley NPC pada kecepatan tinggi.")]
    [SerializeField] private int npcTrolleyCollisionDamage = 1;

    [Tooltip("Jumlah pengurangan HP player saat badan player ditabrak Trolley NPC pada kecepatan tinggi.")]
    [SerializeField] private int playerHpDamageFromNpc = 1;

    [Header("Cooldown Settings")]
    [Tooltip("Waktu tunggu (cooldown) minimal setelah terkena damage sebelum bisa terkena damage lagi (mencegah jitter).")]
    [SerializeField] private float damageCooldown = 1.0f;

    [Header("Collision Configs")]
    [Tooltip("Rasio kecepatan minimum (0-1) dari kecepatan maksimum acuan untuk memicu kerusakan (default 75% yaitu 0.75).")]
    [SerializeField] private float damageSpeedRatioThreshold = 0.75f;

    [Tooltip("Nama GameObject collider Player untuk deteksi tabrakan tubuh player.")]
    [SerializeField] private string playerColliderName = "Player";

    [Tooltip("Tag dari objek Player.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Tag objek dinding yang bisa dirusak jika ditabrak.")]
    [SerializeField] private string wallTag = "Wall";

    [Tooltip("Tag dari objek Trolley NPC.")]
    [SerializeField] private string npcTrolleyTag = "NPCTrolley";

    private TrolleyController playerTrolleyController;
    private float lastDamageTime = -10f; // Diset negatif agar bisa langsung menerima damage di awal

    private void Start()
    {
        // Ambil referensi controller trolley milik player untuk mengecek kecepatan bergeraknya
        playerTrolleyController = GetComponent<TrolleyController>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        // LOGIC DI BALIK LAYAR (Pencegahan Jitter Damage):
        // Jika belum melewati masa cooldown dari damage sebelumnya, kita langsung return/skip.
        // Ini mencegah HP/Durabilitas langsung terkuras habis ketika trolley menabrak dan bergeser (jitter/slide)
        // di dinding yang sama dalam frame berturut-turut.
        if (Time.time < lastDamageTime + damageCooldown)
        {
            return;
        }

        // LOGIC DI BALIK LAYAR (Pemeriksaan Titik Kontak Tabrakan):
        // Unity mengirimkan data tabrakan beserta titik kontak fisik (contacts). 
        // Melalui kontak pertama, kita dapat memeriksa collider mana dari model compound kita 
        // (apakah player atau keranjang belanja) yang pertama kali menerima benturan.
        if (collision.contacts.Length == 0) return;

        Collider thisCollider = collision.contacts[0].thisCollider;
        GameObject otherObj = collision.gameObject;

        // LOGIC DI BALIK LAYAR (Deteksi Sub-Collider):
        // Menggunakan variabel nama dan tag yang dikonfigurasi di Inspector agar fleksibel jika ada perubahan aset.
        bool isPlayerHit = thisCollider.gameObject.name == playerColliderName || thisCollider.CompareTag(playerTag);

        if (isPlayerHit)
        {
            // 1. LOGIKA KERUSAKAN HP PLAYER (Hanya berkurang jika ditabrak oleh Trolley NPC pada kecepatan tinggi)
            if (otherObj.CompareTag(npcTrolleyTag))
            {
                float npcSpeed = GetNpcTrolleySpeed(otherObj);
                float speedRatio = npcSpeed / npcMaxSpeed;

                if (speedRatio >= damageSpeedRatioThreshold)
                {
                    Debug.LogWarning("[TrolleyCollisionHandler] Player ditabrak oleh Trolley NPC pada kecepatan tinggi!");
                    if (HealthManager.Instance != null)
                    {
                        HealthManager.Instance.ReducePlayerHp(playerHpDamageFromNpc);
                        lastDamageTime = Time.time; // Aktifkan cooldown
                    }
                }
            }
        }
        else
        {
            // 2. LOGIKA KERUSAKAN TROLLEY DURABILITY
            
            // KONDISI A: Player menabrak dinding (Tag "Wall")
            if (otherObj.CompareTag(wallTag))
            {
                // Kecepatan trolley player harus >= batas kecepatan berbahaya yang disetel di Inspector
                if (playerTrolleyController != null && playerTrolleyController.CurrentSpeedRatio >= damageSpeedRatioThreshold)
                {
                    Debug.LogWarning("[TrolleyCollisionHandler] Trolley menabrak dinding pada kecepatan tinggi!");
                    if (HealthManager.Instance != null)
                    {
                        HealthManager.Instance.ReduceTrolleyDurability(wallCollisionDamage);
                        lastDamageTime = Time.time; // Aktifkan cooldown
                    }
                }
            }
            // KONDISI B: Trolley Player ditabrak oleh Trolley NPC (Tag "NPCTrolley")
            else if (otherObj.CompareTag(npcTrolleyTag))
            {
                float npcSpeed = GetNpcTrolleySpeed(otherObj);
                float speedRatio = npcSpeed / npcMaxSpeed;

                if (speedRatio >= damageSpeedRatioThreshold)
                {
                    Debug.LogWarning("[TrolleyCollisionHandler] Trolley Player ditabrak oleh Trolley NPC pada kecepatan tinggi!");
                    if (HealthManager.Instance != null)
                    {
                        HealthManager.Instance.ReduceTrolleyDurability(npcTrolleyCollisionDamage);
                        lastDamageTime = Time.time; // Aktifkan cooldown
                    }
                }
            }
        }
    }

    /// <summary>
    /// Mencari tahu kecepatan saat ini dari Trolley NPC secara aman menggunakan kecepatan linier Rigidbody.
    /// </summary>
    private float GetNpcTrolleySpeed(GameObject npcObj)
    {
        // LOGIC DI BALIK LAYAR (Scalable & Decoupled Speed Check):
        // Mengambil Rigidbody dari objek penabrak (atau parent/children-nya jika ada nested structure)
        // untuk mengukur besar magnitude velocity di world space.
        // Cara ini sangat aman karena tidak bergantung pada script navigasi NPC tertentu yang belum dibuat.
        Rigidbody npcRb = npcObj.GetComponent<Rigidbody>();
        if (npcRb == null) npcRb = npcObj.GetComponentInParent<Rigidbody>();
        if (npcRb == null) npcRb = npcObj.GetComponentInChildren<Rigidbody>();

        if (npcRb != null)
        {
            return npcRb.velocity.magnitude;
        }

        return 0f;
    }
}
