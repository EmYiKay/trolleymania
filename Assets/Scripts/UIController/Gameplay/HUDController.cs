using System.Collections;
using UnityEngine;

/// <summary>
/// Script ini berfungsi sebagai pengendali UI HUD (Heads-Up Display) khususnya untuk tombol "Grab" dan "Throw".
/// Bertanggung jawab atas pengelolaan transisi fisik perpindahan barang (Goods) ke Trolley serta persenjataan (Weapon) ke Player,
/// penanganan penumpukan senjata (maksimal 1 senjata), dan kalkulasi gaya lemparan fisika.
/// </summary>
public class HUDController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Referensi ke controller interaksi untuk mendapatkan objek terdekat yang sedang di-highlight.")]
    [SerializeField] private TrolleyInteractController interactController;

    [Tooltip("Titik penempatan barang masuk di dalam keranjang Trolley (ObjSpawnPoint).")]
    [SerializeField] private Transform objSpawnPoint;

    [Tooltip("Titik penempatan senjata di tangan Player (WeaponSpawnPoint).")]
    [SerializeField] private Transform weaponSpawnPoint;

    [Tooltip("Transform Player untuk acuan arah hadap pelemparan senjata.")]
    [SerializeField] private Transform playerTransform;

    [Header("Settings")]
    [Tooltip("Kecepatan gerak linear objek saat melayang menuju titik spawn.")]
    [SerializeField] private float grabMoveSpeed = 8f;

    [Tooltip("Besar gaya dorong impuls saat melempar senjata.")]
    [SerializeField] private float throwForce = 25f;

    [Header("Menu & Pause UI References")]
    [Tooltip("Panel UI kemenangan (BGYouWin)")]
    [SerializeField] private GameObject bgYouWinPanel;

    [Tooltip("Panel UI Pause (BGPause)")]
    [SerializeField] private GameObject bgPausePanel;

    [Tooltip("Nama scene Main Menu untuk di-load ketika quit.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    // Menyimpan referensi senjata yang sedang dipegang aktif oleh player.
    // Jika player mengambil senjata baru, referensi ini digunakan untuk menjatuhkan senjata lama terlebih dahulu.
    private GameObject equippedWeapon = null;

    private void Start()
    {
        // LOGIC DI BALIK LAYAR:
        // Setiap kali game di-start (atau di-load ulang), pastikan Time.timeScale bernilai 1.0f 
        // agar game berjalan normal (tidak dalam kondisi ter-pause).
        Time.timeScale = 1f;

        // Nonaktifkan panel pause dan panel you win di awal agar bersih
        if (bgPausePanel != null)
        {
            bgPausePanel.SetActive(false);
        }

        if (bgYouWinPanel != null)
        {
            bgYouWinPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Metode publik yang dihubungkan ke Event On Click tombol "Grab" di UI.
    /// </summary>
    public void Grab()
    {
        // 1. Validasi referensi interaksi
        if (interactController == null)
        {
            Debug.LogWarning("HUDController: interactController belum dihubungkan di Inspector!");
            return;
        }

        // 2. Ambil objek yang sedang di-highlight
        Outline targetOutline = interactController.CurrentHighlightedOutline;
        if (targetOutline == null)
        {
            Debug.Log("Tidak ada objek terdekat dalam jangkauan interaksi untuk diambil.");
            return;
        }

        GameObject targetObj = targetOutline.gameObject;
        string targetTag = targetObj.tag;

        // 3. Hapus segera dari daftar kandidat agar efek highlight mati dan tidak terpilih kembali saat melayang
        interactController.RemoveCandidate(targetOutline);

        // 4. Proses berdasarkan tag objek
        if (targetTag == "Goods")
        {
            // Kirim barang ke keranjang trolley
            StartCoroutine(MoveToTargetCoroutine(targetObj, objSpawnPoint, true));
        }
        else if (targetTag == "Weapon")
        {
            // LOGIC DI BALIK LAYAR (Pengecekan Slot Senjata Maksimal 1):
            // Jika ada senjata yang sedang dipegang, jatuhkan senjata lama ke tanah secara fisik
            // sebelum menarik senjata yang baru.
            if (equippedWeapon != null)
            {
                DropCurrentWeapon();
            }
            else if (weaponSpawnPoint.childCount > 0)
            {
                // Fallback: Bersihkan semua objek anak yang tidak sengaja menempel di spawn point senjata
                foreach (Transform child in weaponSpawnPoint)
                {
                    DropWeaponPhysics(child.gameObject);
                }
            }

            // Daftarkan senjata baru ini ke slot pegangan player
            equippedWeapon = targetObj;

            // Tarik senjata ke titik genggam player
            StartCoroutine(MoveToTargetCoroutine(targetObj, weaponSpawnPoint, false));
        }
    }

    /// <summary>
    /// Metode publik yang dihubungkan ke Event On Click tombol "Throw" di UI.
    /// </summary>
    public void Throw()
    {
        // Pengecekan fallback jika referensi internal kosong namun ada objek anak secara fisik di WeaponSpawnPoint
        if (equippedWeapon == null && weaponSpawnPoint.childCount > 0)
        {
            equippedWeapon = weaponSpawnPoint.GetChild(0).gameObject;
        }

        // Jika ada senjata yang sedang dibawa
        if (equippedWeapon != null)
        {
            GameObject weaponToThrow = equippedWeapon;
            equippedWeapon = null; // Kosongkan slot genggaman tangan player

            // 1. Lepaskan parent dari player agar posisinya mandiri di world space
            weaponToThrow.transform.SetParent(null);

            // 2. Aktifkan kembali semua collider pada senjata agar dapat menabrak objek lain di scene
            Collider[] colliders = weaponToThrow.GetComponentsInChildren<Collider>();
            foreach (Collider col in colliders)
            {
                col.enabled = true;
            }

            // 3. Aktifkan kembali mesin simulasi Rigidbody fisika
            Rigidbody rb = weaponToThrow.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                // LOGIC DI BALIK LAYAR (Arah dan Lintasan Lemparan):
                // Tentukan arah hadap lemparan. Kita mengambil arah forward player.
                // Ditambahkan sedikit offset vertikal (Vector3.up * 0.15f) agar membentuk lintasan parabolik (melengkung ke atas lalu jatuh),
                // memberikan efek visual lemparan yang terasa realistis dan mantap.
                Vector3 throwDir = playerTransform != null ? playerTransform.forward : Vector3.forward;
                Vector3 finalForce = (throwDir + Vector3.up * 0.15f).normalized * throwForce;

                // Terapkan gaya impuls instan (ForceMode.Impulse) yang ideal untuk simulasi lemparan/tembakan
                rb.AddForce(finalForce, ForceMode.Impulse);
            }
        }
        else
        {
            Debug.Log("Pemain tidak memegang senjata apapun untuk dilempar!");
        }
    }

    /// <summary>
    /// Coroutine untuk menggerakkan objek secara perlahan dengan kecepatan linear menuju target spawn.
    /// </summary>
    private IEnumerator MoveToTargetCoroutine(GameObject obj, Transform target, bool isGoods)
    {
        // LOGIC DI BALIK LAYAR (Mengatasi Tabrakan & Fisika Selama Transisi):
        // Jika kita langsung menarik objek yang memiliki Collider & Rigidbody aktif, objek tersebut akan menabrak rak supermarket,
        // lantai, atau bahkan bodi trolley itu sendiri sepanjang jalan. Hal ini mengakibatkan efek getar (jitter) yang merusak visual.
        // Solusi:
        // 1. Matikan seluruh Collider yang ada pada objek (dan anak-anaknya).
        // 2. Jadikan Rigidbody bersifat 'isKinematic = true' agar tidak terpengaruh oleh gaya gravitasi atau tumbukan fisika luar.
        Collider[] colliders = obj.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = false;
        }

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // Gerakkan secara perlahan dengan kecepatan linear (Vector3.MoveTowards) hingga sangat dekat dengan target
        while (obj != null && Vector3.Distance(obj.transform.position, target.position) > 0.05f)
        {
            // Vector3.MoveTowards menghasilkan pergerakan linear yang stabil (kecepatan konstan)
            obj.transform.position = Vector3.MoveTowards(obj.transform.position, target.position, grabMoveSpeed * Time.deltaTime);
            yield return null;
        }

        // Pastikan objek tepat berada di posisi target
        if (obj != null)
        {
            obj.transform.position = target.position;

            // Tempelkan objek menjadi anak (child) dari spawn point agar posisinya mengikuti pergerakan trolley/player
            obj.transform.SetParent(target);

            if (isGoods)
            {
                // LOGIC DI BALIK LAYAR (Barang Masuk Trolley):
                // Jika itu barang belanjaan (Goods), nyalakan kembali fisika dan collider-nya
                // agar ia langsung jatuh bebas secara alami dan menumpuk di dalam keranjang trolley.
                foreach (Collider col in colliders)
                {
                    col.enabled = true;
                }

                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
            else
            {
                // LOGIC DI BALIK LAYAR (Senjata Digenggam):
                // Jika itu senjata (Weapon), biarkan collider & Rigidbody TETAP MATI agar senjata tersebut
                // menempel sempurna di tangan pemain tanpa jatuh ke tanah akibat gravitasi.
                obj.transform.localRotation = Quaternion.identity; // Reset rotasi lokal agar posisi senjata rapi di genggaman
            }
        }
    }

    /// <summary>
    /// Menjatuhkan senjata yang sedang aktif dipegang ke tanah secara fisik.
    /// </summary>
    private void DropCurrentWeapon()
    {
        if (equippedWeapon == null) return;

        DropWeaponPhysics(equippedWeapon);
        equippedWeapon = null;
    }

    /// <summary>
    /// Mengaktifkan kembali collider dan rigidbody pada senjata agar jatuh bebas ke tanah secara fisik.
    /// </summary>
    private void DropWeaponPhysics(GameObject weapon)
    {
        // 1. Putuskan hubungan parent dari player
        weapon.transform.SetParent(null);

        // 2. Aktifkan kembali collider
        Collider[] colliders = weapon.GetComponentsInChildren<Collider>();
        foreach (Collider col in colliders)
        {
            col.enabled = true;
        }

        // 3. Aktifkan simulasi fisika Rigidbody dan berikan sedikit dorongan jatuh bebas
        Rigidbody rb = weapon.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            // Dorong sedikit ke arah belakang-atas player agar visual jatuhnya terpisah dari badan player
            Vector3 pushDirection = playerTransform != null ? -playerTransform.forward : Vector3.back;
            rb.AddForce(pushDirection * 1.5f + Vector3.up * 1f, ForceMode.Impulse);
        }
    }

    /// <summary>
    /// Membuka panel Pause dan menghentikan jalannya waktu permainan (Time.timeScale = 0).
    /// </summary>
    public void PauseGame()
    {
        if (bgPausePanel != null)
        {
            bgPausePanel.SetActive(true);
        }
        
        // LOGIC DI BALIK LAYAR:
        // Menyetel timeScale ke 0 akan membekukan jalannya fisika dan update berbasis waktu (Time.deltaTime),
        // secara efektif mem-pause gameplay.
        Time.timeScale = 0f;
        Debug.Log("[HUDController] Game Paused.");
    }

    /// <summary>
    /// Diaktifkan ketika tombol "Return" ditekan. Menutup panel pause dan mengembalikan jalannya waktu.
    /// </summary>
    public void ResumeGame()
    {
        if (bgPausePanel != null)
        {
            bgPausePanel.SetActive(false);
        }
        
        // LOGIC DI BALIK LAYAR:
        // Mengembalikan timeScale ke 1.0f agar simulasi fisika dan jalannya permainan kembali normal.
        Time.timeScale = 1f;
        Debug.Log("[HUDController] Game Resumed.");
    }

    /// <summary>
    /// Diaktifkan ketika tombol "Yes" (Play Again) di BGYouWin ditekan. 
    /// Memuat ulang scene aktif saat ini secara bersih.
    /// </summary>
    public void PlayAgain()
    {
        // LOGIC DI BALIK LAYAR:
        // Pastikan timeScale disetel ke 1.0f sebelum berpindah scene, agar game tidak membeku.
        Time.timeScale = 1f;
        
        // Mengambil build index dari scene yang sedang aktif saat ini untuk dimuat ulang.
        int activeSceneIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
        UnityEngine.SceneManagement.SceneManager.LoadScene(activeSceneIndex);
        
        Debug.Log("[HUDController] Re-loading active gameplay scene.");
    }

    /// <summary>
    /// Diaktifkan ketika tombol "No" (BGYouWin) atau "Quit" (BGPause) ditekan.
    /// Memuat scene Main Menu.
    /// </summary>
    public void QuitToMainMenu()
    {
        // LOGIC DI BALIK LAYAR:
        // Sangat penting untuk mengembalikan timeScale ke 1.0f sebelum berpindah ke Main Menu,
        // jika tidak, UI, tombol, atau animasi di Main Menu tidak akan bisa berinteraksi karena ter-pause.
        Time.timeScale = 1f;
        
        // Memuat scene menu utama menggunakan nama scene yang dikonfigurasi di Inspector.
        UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuSceneName);
        
        Debug.Log($"[HUDController] Loading Main Menu scene: '{mainMenuSceneName}'.");
    }
}
