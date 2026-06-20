using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Class untuk menyimpan status barang belanjaan yang harus diambil dalam list belanja.
/// </summary>
[System.Serializable]
public class ObjectiveItem
{
    public string itemName;      // Nama barang belanjaan (sesuai ObjName pada ObjectScript)
    public int requiredAmount;   // Jumlah target yang harus diambil
    public int currentAmount;    // Jumlah saat ini yang sudah masuk trolley
}

/// <summary>
/// Manager pusat untuk memproses spawn barang secara asynchronous (perlahan) di awal permainan,
/// mengacak tugas belanja (Objective), dan memperbarui seluruh UI terkait belanjaan.
/// </summary>
public class ObjectiveManager : MonoBehaviour
{
    public static ObjectiveManager Instance { get; private set; }

    [Header("Spawning Settings")]
    [Tooltip("Daftar prefab objek (Goods / Weapon) yang siap di-spawn ke rak-rak supermarket.")]
    [SerializeField] private GameObject[] prefabsToSpawn;

    [Tooltip("Parent transform yang berisi semua titik spawn (spawn points).")]
    [SerializeField] private Transform spawnPointsRoot;

    [Tooltip("Waktu jeda (detik) antar setiap spawn objek untuk meminimalkan beban CPU (terutama di mobile).")]
    [SerializeField] private float spawnDelay = 0.05f;

    [Header("UI Preview References")]
    [Tooltip("GameObject Text untuk nama Goods 1.")]
    [SerializeField] private GameObject goods1NameText;
    [Tooltip("GameObject Text untuk progres/jumlah Goods 1.")]
    [SerializeField] private GameObject goods1ProgressText;

    [Tooltip("GameObject Text untuk nama Goods 2.")]
    [SerializeField] private GameObject goods2NameText;
    [Tooltip("GameObject Text untuk progres/jumlah Goods 2.")]
    [SerializeField] private GameObject goods2ProgressText;

    [Header("UI List References")]
    [Tooltip("Panel latar belakang dari seluruh shopping list (BGListObjective).")]
    [SerializeField] private GameObject bgListObjective;

    [Tooltip("GameObject Text di dalam panel list untuk mencetak seluruh daftar barang belanjaan.")]
    [SerializeField] private GameObject listText;

    // List internal yang menampung daftar belanja acak pemain
    private List<ObjectiveItem> objectives = new List<ObjectiveItem>();

    private void Awake()
    {
        // PENTING: Setel ulang timeScale ke 1.0f setiap kali scene di-load. 
        // Ini mencegah game tetap ter-pause ketika scene di-restart dari menu 'PlayAgain'.
        Time.timeScale = 1f;

        // LOGIC DI BALIK LAYAR (Singleton Pattern):
        // Memastikan hanya ada satu Instance ObjectiveManager di dalam permainan agar mudah diakses oleh script lain.
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // Menonaktifkan panel list belanja di awal permainan secara default
        if (bgListObjective != null)
        {
            bgListObjective.SetActive(false);
        }

        // LOGIC DI BALIK LAYAR:
        // Sesuai alur yang baru, kita acak terlebih dahulu rancangan list belanjaan (objective) pemain.
        // Berdasarkan list tersebut, kita tahu barang apa saja yang HARUS disiapkan di rak supermarket
        // agar permainan dijamin dapat diselesaikan (solvable).
        GenerateRandomObjectives();
    }

    /// <summary>
    /// Membuat misi belanja acak dengan total target barang antara 5 sampai 8 unit dari prefabsToSpawn.
    /// </summary>
    private void GenerateRandomObjectives()
    {
        // 1. Kumpulkan semua prefab unik bertipe Goods dari prefabsToSpawn sebelum barang di-spawn ke scene
        List<GameObject> availableGoodsPrefabs = new List<GameObject>();
        List<string> uniqueGoodsNames = new List<string>();

        foreach (GameObject prefab in prefabsToSpawn)
        {
            if (prefab == null) continue;

            ObjectScript objScript = prefab.GetComponent<ObjectScript>();
            if (objScript == null) objScript = prefab.GetComponentInParent<ObjectScript>();
            if (objScript == null) objScript = prefab.GetComponentInChildren<ObjectScript>();

            if (objScript != null && objScript.KindOfObject == ObjectKind.Goods)
            {
                if (!uniqueGoodsNames.Contains(objScript.ObjName))
                {
                    uniqueGoodsNames.Add(objScript.ObjName);
                    availableGoodsPrefabs.Add(prefab);
                }
            }
        }

        if (availableGoodsPrefabs.Count == 0)
        {
            Debug.LogError("ObjectiveManager: Tidak ditemukan prefab bertipe Goods di array prefabsToSpawn!");
            return;
        }

        // 2. Acak urutan daftar prefab Goods yang tersedia
        for (int i = 0; i < availableGoodsPrefabs.Count; i++)
        {
            GameObject tempPrefab = availableGoodsPrefabs[i];
            int randomIndex = Random.Range(i, availableGoodsPrefabs.Count);
            availableGoodsPrefabs[i] = availableGoodsPrefabs[randomIndex];
            availableGoodsPrefabs[randomIndex] = tempPrefab;
        }

        // 3. Tentukan jumlah total target belanja acak (5 sampai 8)
        int targetTotal = Random.Range(5, 9);
        int remainingAmount = targetTotal;

        // Ambil maksimal 3 jenis barang agar daftar belanja tidak terlalu rumit
        int numTypes = Mathf.Min(3, availableGoodsPrefabs.Count);
        
        // Bersihkan list objective lama
        objectives.Clear();

        // List pembantu untuk menampung prefab barang belanjaan yang wajib di-spawn
        List<GameObject> requiredSpawns = new List<GameObject>();

        // 4. Distribusikan jumlah target secara acak ke barang yang terpilih
        for (int i = 0; i < numTypes; i++)
        {
            GameObject chosenPrefab = availableGoodsPrefabs[i];
            ObjectScript objScript = chosenPrefab.GetComponent<ObjectScript>();
            if (objScript == null) objScript = chosenPrefab.GetComponentInParent<ObjectScript>();
            if (objScript == null) objScript = chosenPrefab.GetComponentInChildren<ObjectScript>();

            ObjectiveItem item = new ObjectiveItem();
            item.itemName = objScript.ObjName;

            if (i == numTypes - 1)
            {
                // Item terakhir menampung seluruh sisa target
                item.requiredAmount = remainingAmount;
            }
            else
            {
                // Berikan jumlah minimal 1, dan maksimal disisakan 1 untuk setiap slot berikutnya
                int maxPossible = remainingAmount - (numTypes - 1 - i);
                item.requiredAmount = Random.Range(1, maxPossible + 1);
            }

            item.currentAmount = 0;
            remainingAmount -= item.requiredAmount;
            objectives.Add(item);

            // Masukkan prefab barang belanjaan ini sebanyak target belanja ke list wajib spawn
            for (int k = 0; k < item.requiredAmount; k++)
            {
                requiredSpawns.Add(chosenPrefab);
            }
        }

        // 5. Mulai proses spawn bertahap dengan mengirimkan daftar barang belanjaan wajib
        StartCoroutine(SpawnObjectsSlowly(requiredSpawns));
    }

    /// <summary>
    /// Coroutine untuk men-spawn objek satu per satu secara perlahan.
    /// Menjamin semua barang belanjaan wajib di-spawn terlebih dahulu, baru mengisi sisa titik spawn dengan barang acak.
    /// </summary>
    private IEnumerator SpawnObjectsSlowly(List<GameObject> requiredSpawns)
    {
        if (spawnPointsRoot == null || prefabsToSpawn == null || prefabsToSpawn.Length == 0)
        {
            Debug.LogWarning("ObjectiveManager: SpawnPoints atau PrefabsToSpawn kosong!");
            yield break;
        }

        int totalSpawnPoints = spawnPointsRoot.childCount;

        // 1. Buat daftar antrean spawn kosong
        List<GameObject> spawnQueue = new List<GameObject>();

        // 2. Masukkan seluruh barang belanjaan wajib (objective) ke dalam antrean spawn
        //    PENTING: Kita batasi agar jumlah barang wajib tidak melebihi kapasitas titik spawn di map
        int requiredCount = Mathf.Min(requiredSpawns.Count, totalSpawnPoints);
        for (int i = 0; i < requiredCount; i++)
        {
            spawnQueue.Add(requiredSpawns[i]);
        }

        // 3. Isi sisa titik spawn kosong di supermarket dengan prefab acak dari daftar prefabsToSpawn
        int remainingSlots = totalSpawnPoints - spawnQueue.Count;
        for (int i = 0; i < remainingSlots; i++)
        {
            GameObject randomPrefab = prefabsToSpawn[Random.Range(0, prefabsToSpawn.Length)];
            spawnQueue.Add(randomPrefab);
        }

        // 4. LOGIC DI BALIK LAYAR (Pencegahan Pola Spawn Berkumpul / Shuffling):
        //    Jika kita langsung melakukan instansiasi, barang-barang wajib belanjaan akan menumpuk di area awal (spawn points index awal).
        //    Solusi: Kita lakukan pengacakan urutan (Shuffle) pada antrean menggunakan Fisher-Yates shuffle algorithm.
        //    Ini menjamin persebaran barang belanjaan menyebar rata secara random di seluruh supermarket.
        for (int i = 0; i < spawnQueue.Count; i++)
        {
            GameObject temp = spawnQueue[i];
            int randomIndex = Random.Range(i, spawnQueue.Count);
            spawnQueue[i] = spawnQueue[randomIndex];
            spawnQueue[randomIndex] = temp;
        }

        // 5. Lakukan spawn secara perlahan satu per satu menggunakan jeda frame
        for (int i = 0; i < totalSpawnPoints; i++)
        {
            Transform spawnPoint = spawnPointsRoot.GetChild(i);
            GameObject prefab = spawnQueue[i];
            
            if (prefab != null)
            {
                Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
            }

            // Berikan jeda frame agar CPU dapat bernapas
            yield return new WaitForSeconds(spawnDelay);
        }

        // 6. Perbarui tampilan UI belanja untuk pertama kalinya setelah seluruh barang ter-spawn
        UpdateAllUI();
    }

    /// <summary>
    /// Dipanggil dari TrolleyAreaDetector ketika ada barang belanjaan yang masuk (+1) atau keluar (-1) dari trolley.
    /// </summary>
    public void UpdateObjectiveProgress(string itemName, int amountChange)
    {
        // Cari barang yang sesuai di dalam list objective
        ObjectiveItem targetItem = objectives.Find(x => x.itemName == itemName);
        
        if (targetItem != null)
        {
            // Perbarui jumlah saat ini (dibatasi tidak boleh kurang dari 0 atau melebihi batas maksimal)
            targetItem.currentAmount = Mathf.Clamp(targetItem.currentAmount + amountChange, 0, targetItem.requiredAmount + 100);
            
            // Perbarui tampilan UI secara menyeluruh
            UpdateAllUI();
        }
    }

    /// <summary>
    /// Memeriksa apakah seluruh misi belanja belanjaan telah berhasil diselesaikan (currentAmount >= requiredAmount).
    /// </summary>
    public bool AreAllObjectivesCompleted()
    {
        // Jika list belanja kosong, berarti tidak ada misi
        if (objectives == null || objectives.Count == 0) return false;

        foreach (var item in objectives)
        {
            // Jika ada satu saja barang yang jumlahnya di bawah target, maka belum selesai
            if (item.currentAmount < item.requiredAmount)
            {
                return false;
            }
        }

        // Semua target barang belanjaan telah terpenuhi!
        return true;
    }

    /// <summary>
    /// Menyalakan / mematikan panel BGListObjective (Toggle).
    /// </summary>
    public void ToggleBGListObjective()
    {
        if (bgListObjective != null)
        {
            bgListObjective.SetActive(!bgListObjective.activeSelf);
            
            // PENTING: Setiap kali list dibuka, pastikan UI list diperbarui
            if (bgListObjective.activeSelf)
            {
                UpdateFullListUI();
            }
        }
    }

    /// <summary>
    /// Memperbarui seluruh visual UI (Quick Preview dan Full List).
    /// </summary>
    private void UpdateAllUI()
    {
        UpdateQuickPreviewUI();
        UpdateFullListUI();
    }

    /// <summary>
    /// Memperbarui tampilan 2 barang di Quick Preview (goods1 & goods2).
    /// Menampilkan barang yang BELUM selesai dikumpulkan, diurutkan berdasarkan persentase kemajuan terkecil.
    /// </summary>
    private void UpdateQuickPreviewUI()
    {
        // 1. Pisahkan barang yang belum selesai dengan barang yang sudah selesai
        List<ObjectiveItem> incomplete = new List<ObjectiveItem>();
        List<ObjectiveItem> completed = new List<ObjectiveItem>();

        foreach (var item in objectives)
        {
            if (item.currentAmount < item.requiredAmount)
            {
                incomplete.Add(item);
            }
            else
            {
                completed.Add(item);
            }
        }

        // 2. Urutkan yang belum selesai berdasarkan persentase kemajuan terendah (agar prioritas barang tersulit ditampilkan dulu)
        incomplete.Sort((a, b) => ((float)a.currentAmount / a.requiredAmount).CompareTo((float)b.currentAmount / b.requiredAmount));

        // 3. Satukan kembali daftar display (yang belum selesai tampil paling depan)
        List<ObjectiveItem> sortedDisplay = new List<ObjectiveItem>();
        sortedDisplay.AddRange(incomplete);
        sortedDisplay.AddRange(completed);

        // 4. Update Slot 1
        if (sortedDisplay.Count >= 1)
        {
            SetText(goods1NameText, sortedDisplay[0].itemName);
            SetText(goods1ProgressText, $"({sortedDisplay[0].currentAmount}/{sortedDisplay[0].requiredAmount})");
        }
        else
        {
            SetText(goods1NameText, "-");
            SetText(goods1ProgressText, "(0/0)");
        }

        // 5. Update Slot 2
        if (sortedDisplay.Count >= 2)
        {
            SetText(goods2NameText, sortedDisplay[1].itemName);
            SetText(goods2ProgressText, $"({sortedDisplay[1].currentAmount}/{sortedDisplay[1].requiredAmount})");
        }
        else
        {
            SetText(goods2NameText, "-");
            SetText(goods2ProgressText, "(0/0)");
        }
    }

    /// <summary>
    /// Memperbarui tampilan list lengkap belanjaan di BGListObjective.
    /// </summary>
    private void UpdateFullListUI()
    {
        if (listText == null) return;

        string fullText = "";
        for (int i = 0; i < objectives.Count; i++)
        {
            var item = objectives[i];
            bool isCompleted = item.currentAmount >= item.requiredAmount;
            // Gunakan warna hijau jika selesai (menggunakan 'V' karena karakter unicode centang tidak ter-render oleh font TMPro default), dan orange jika belum
            string prefix = isCompleted ? "<color=green>[V]</color>" : "<color=orange>[-]</color>";
            fullText += $"{prefix} {item.itemName} ({item.currentAmount}/{item.requiredAmount})\n";
        }

        SetText(listText, fullText);
    }

    /// <summary>
    /// Fungsi pembantu (helper) untuk memperbarui string text pada GameObject UI,
    /// secara dinamis mendeteksi TextMeshProUGUI maupun UI Text standar Unity.
    /// </summary>
    private void SetText(GameObject go, string content)
    {
        if (go == null) return;

        // Coba deteksi komponen TextMeshProUGUI
        var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.text = content;
            return;
        }

        // Coba deteksi komponen UI Text biasa
        var txt = go.GetComponent<UnityEngine.UI.Text>();
        if (txt != null)
        {
            txt.text = content;
        }
    }
}
