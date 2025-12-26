using System.Collections;
using System.Collections.Generic;
using Unity.Sentis; // Senin sürümün burayı kullanıyor
using UnityEngine;
using UnityEngine.UI;

public class PuzzleController : MonoBehaviour
{
    [Header("Yapay Zeka Beyni (ONNX)")]
    public ModelAsset brainModel;
    private Worker worker;

    [Header("Yönetici Kontrolleri")]
    public bool isAIPlaying = false;

    // SENİN SÜRÜMÜNE UYGUN TENSÖR TİPİ
    private Tensor<float> gridTensor;
    private Tensor<float> inventoryTensor;

    // Otomatik İsimler
    private string inputName1 = "grid";
    private string inputName2 = "inventory";
    private List<string> outputNames = new List<string>();

    [Header("Bağlantılar")]
    public GridManager gridManager;
    // countTexts'i kaldırdık çünkü artık MenuController yönetiyor, ama hata vermesin diye boş bırakabilirsin
    // public Text[] countTexts; 

    [Header("Ayarlar")]
    public float moveDelay = 0.3f;

    private int[,] currentGrid = new int[4, 6];
    public int[] currentInventory = new int[6];

    private int currentScore = 0;
    private MenuController menuController; // Referans lazım

    // Şekil Tanımları
    private readonly List<Vector2Int[]> shapes = new List<Vector2Int[]>
    {
        new Vector2Int[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0) },
        new Vector2Int[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,0), new Vector2Int(1,1) },
        new Vector2Int[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(1,2) },
        new Vector2Int[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(1,1) },
        new Vector2Int[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,1) },
        new Vector2Int[] { new Vector2Int(0,0) }
    };

    private readonly int[,] shapeDimensions = new int[,] { { 3, 1 }, { 2, 2 }, { 2, 3 }, { 2, 2 }, { 2, 2 }, { 1, 1 } };

    void Start()
    {
        if (brainModel == null) { Debug.LogError("🚨 Brain Model BOŞ!"); return; }

        menuController = FindObjectOfType<MenuController>();

        Model model = ModelLoader.Load(brainModel);

        if (model.inputs.Count >= 2)
        {
            inputName1 = model.inputs[0].name;
            inputName2 = model.inputs[1].name;
        }

        outputNames.Clear();
        foreach (var outDef in model.outputs) outputNames.Add(outDef.name);

        // SENİN SÜRÜMÜNE UYGUN WORKER OLUŞTURMA
        worker = new Worker(model, BackendType.CPU);

        // DİKKAT: StartGame() fonksiyonunu sildik. Artık menüden emir bekliyor.
    }

    // --- YENİ EKLENEN FONKSİYON (MenuController BURAYI ÇAĞIRACAK) ---
    public void StartGameLogic()
    {
        if (gridManager != null) gridManager.InitializeGrid();
        // 1. Grid'i Sıfırla
        currentGrid = new int[4, 6];
        if (gridManager != null) gridManager.InitializeGrid();
        currentGrid = new int[4, 6];
        gridManager.UpdateVisuals(currentGrid);
        System.Array.Copy(GameSettings.SelectedInventory, currentInventory, 6);

        isAIPlaying = false; // Oyun başında AI durur, komut bekler
        StopAllCoroutines();
        Debug.Log("Oyun hazır. İster Step yap, ister Start ile AI'ı sal.");
        currentScore = 0;
        if (menuController != null) menuController.UpdateScoreUI(currentScore);
    }
    public void StartAIAutoPlay()
    {
        if (!isAIPlaying)
        {
            isAIPlaying = true;
            StartCoroutine(AIGameLoopManual());
        }
    }
    public void StopAIAutoPlay()
    {
        isAIPlaying = false;
        StopAllCoroutines();
    }
    IEnumerator AIGameLoopManual()
    {
        while (isAIPlaying)
        {
            // Daha önce yazdığımız Tek Hamle fonksiyonunu çağırıyoruz
            MakeSingleAIMove();

            // Eğer hamle bittiyse veya kilitlendiyse dur (MakeSingleAIMove içinde kontrol var)
            yield return new WaitForSeconds(moveDelay);
        }
    }
    // --- OYUN DÖNGÜSÜ (Eski Kodunun Aynısı) ---
    IEnumerator GameLoop()
    {
        bool gameOver = false;
        while (!gameOver)
        {
            List<int> validMoves = new List<int>();
            for (int i = 0; i < 144; i++)
            {
                int sId = i / 24; int rem = i % 24; int r = rem / 6; int c = rem % 6;
                if (IsValidPlacement(sId, r, c)) validMoves.Add(i);
            }

            if (validMoves.Count == 0) { Debug.Log("🏁 Oyun Bitti"); gameOver = true; yield break; }

            int bestAction = -1;
            float[] aiScores = RunAIModel();

            if (aiScores != null)
            {
                float maxScore = float.NegativeInfinity;
                foreach (int moveIndex in validMoves)
                {
                    float score = aiScores[moveIndex];
                    if (!float.IsNaN(score) && score > maxScore)
                    {
                        maxScore = score;
                        bestAction = moveIndex;
                    }
                }
            }

            // Eğer AI bulamazsa rastgele yap (Fallback)
            if (bestAction == -1) bestAction = validMoves[UnityEngine.Random.Range(0, validMoves.Count)];

            int shapeId = bestAction / 24;
            int remaining = bestAction % 24;
            int row = remaining / 6;
            int col = remaining % 6;

            PlaceShape(shapeId, row, col);

            if (currentInventory[shapeId] > 0)
            {
                currentInventory[shapeId]--;
                // Envanter görselini güncellemek istersen buraya kod eklenecek
                // MenuController'a erişim olmadığı için şimdilik boş geçiyoruz.
            }

            yield return new WaitForSeconds(moveDelay);
        }
    }

    // --- SENİN TENSÖR MANTIĞININ AYNISI ---
    float[] RunAIModel()
    {
        try
        {
            float[] gridData = new float[24];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 6; c++)
                    gridData[r * 6 + c] = currentGrid[r, c] > 0 ? 1.0f : 0.0f;

            float[] invData = new float[6];
            for (int i = 0; i < 6; i++) invData[i] = (float)currentInventory[i];

            // ESKİ SÜRÜM TENSÖR OLUŞTURMA
            gridTensor = new Tensor<float>(new TensorShape(1, 1, 4, 6), gridData);
            inventoryTensor = new Tensor<float>(new TensorShape(1, 6), invData);

            worker.SetInput(inputName1, gridTensor);
            worker.SetInput(inputName2, inventoryTensor);
            worker.Schedule();

            Tensor<float> output = null;
            int maxElements = 0;
            foreach (string outName in outputNames)
            {
                var tempOutput = worker.PeekOutput(outName) as Tensor<float>;
                if (tempOutput != null)
                {
                    int currentLen = tempOutput.shape.length;
                    if (currentLen > maxElements) { maxElements = currentLen; output = tempOutput; }
                }
            }

            if (output == null) { gridTensor.Dispose(); inventoryTensor.Dispose(); return null; }
            float[] results = output.DownloadToArray();
            gridTensor.Dispose(); inventoryTensor.Dispose();
            return results;
        }
        catch { return null; }
    }

    // --- YARDIMCI FONKSİYONLAR (Aynen Korundu) ---
    bool IsValidPlacement(int shapeId, int startRow, int startCol)
    {
        if (currentInventory[shapeId] <= 0) return false;
        int h = shapeDimensions[shapeId, 0];
        int w = shapeDimensions[shapeId, 1];
        if (startRow + h > 4) return false;
        if (startCol + w > 6) return false;
        Vector2Int[] coords = shapes[shapeId];
        foreach (Vector2Int p in coords)
        {
            int r = startRow + p.x; int c = startCol + p.y;
            if (currentGrid[r, c] > 0) return false;
        }
        return true;
    }

    void PlaceShape(int shapeId, int startRow, int startCol)
    {
        Vector2Int[] coords = shapes[shapeId];
        foreach (Vector2Int p in coords)
        {
            int r = startRow + p.x; int c = startCol + p.y;
            currentGrid[r, c] = shapeId + 1;
        }
        if (gridManager != null) gridManager.UpdateVisuals(currentGrid);

        foreach (Vector2Int p in shapes[shapeId])
        {
            currentGrid[startRow + p.x, startCol + p.y] = shapeId + 1;

            // HER BİR KARE İÇİN 1 PUAN
            currentScore += 1;
        }

        gridManager.UpdateVisuals(currentGrid);

        // UI GÜNCELLE
        if (menuController != null) menuController.UpdateScoreUI(currentScore);

        // OYUN BİTTİ Mİ KONTROL ET
        CheckGameEnd();
    }

    void OnDisable() { if (worker != null) worker.Dispose(); }
    public void StopGame()
    {
        StopAllCoroutines();
        // Grid'i temizle
        currentGrid = new int[4, 6];
        if (gridManager != null) gridManager.UpdateVisuals(currentGrid);
    }

    public void MakeSingleAIMove()
    {
        // 1. Önce geçerli hamle var mı diye bakalım
        List<int> validMoves = new List<int>();
        for (int i = 0; i < 144; i++)
        {
            int sId = i / 24; int rem = i % 24; int r = rem / 6; int c = rem % 6;
            if (IsValidPlacement(sId, r, c)) validMoves.Add(i);
        }

        if (validMoves.Count == 0)
        {
            Debug.Log("⚠️ Yapılacak hamle kalmadı veya oyun kilitlendi!");
            if (menuController != null)
            {
                menuController.UpdateScoreUI(currentScore); // Son puanı yaz
                menuController.ShowGameOverPanel(currentScore); // Paneli aç
            }

            StopAllCoroutines(); // AI oynuyorsa durdur
            isAIPlaying = false;

            return;
        }

        // 2. Modeli Çalıştır
        float[] aiScores = RunAIModel();
        int bestAction = -1;

        if (aiScores != null)
        {
            float maxScore = float.NegativeInfinity;
            foreach (int moveIndex in validMoves)
            {
                float score = aiScores[moveIndex];
                if (!float.IsNaN(score) && score > maxScore)
                {
                    maxScore = score;
                    bestAction = moveIndex;
                }
            }
        }

        // AI bulamazsa rastgele (Fallback)
        if (bestAction == -1) bestAction = validMoves[UnityEngine.Random.Range(0, validMoves.Count)];

        // 3. Hamleyi Uygula
        int shapeId = bestAction / 24;
        int remaining = bestAction % 24;
        int row = remaining / 6;
        int col = remaining % 6;

        Debug.Log($"🤖 AI Hamle Yaptı: Şekil {shapeId} -> ({row}, {col})");
        PlaceShape(shapeId, row, col);

        // Envanterden düş
        if (currentInventory[shapeId] > 0)
        {
            currentInventory[shapeId]--;

            // UI Güncellemesini Tetikle (PlayerInteraction üzerinden)
            PlayerInteraction pi = FindObjectOfType<PlayerInteraction>();
            if (pi != null) pi.UpdateHandUI(); // Bu fonksiyonu geçen adımda public yapmıştık
        }
    }

    // --- ÇOK ÖNEMLİ EKLEME: SENİN HAMLENİ AI'IN DUYMASI İÇİN ---
    // PlayerInteraction bu fonksiyonu çağıracak
    public void RegisterPlayerMove(int shapeId, int r, int c)
    {
        Vector2Int[] coords = shapes[shapeId];
        foreach (Vector2Int p in coords)
        {
            int targetR = r + p.x;
            int targetC = c + p.y;
            if (targetR >= 0 && targetR < 4 && targetC >= 0 && targetC < 6)
            {
                currentGrid[targetR, targetC] = shapeId + 1;
                // HER KARE İÇİN 1 PUAN
                currentScore += 1;
            }
        }
        // UI GÜNCELLE
        if (menuController != null) menuController.UpdateScoreUI(currentScore);

        // OYUN BİTTİ Mİ KONTROL ET
        CheckGameEnd();
    }

    void CheckGameEnd()
    {
        int filledCount = 0;
        foreach (int cell in currentGrid)
        {
            if (cell > 0) filledCount++;
        }

        // Eğer 24 karenin hepsi doluysa
        if (filledCount == 24)
        {
            Debug.Log("Oyun Bitti! Tam Puan!");
            currentScore += 100; // BONUS

            if (menuController != null)
            {
                menuController.UpdateScoreUI(currentScore); // Son puanı yaz
                menuController.ShowGameOverPanel(currentScore); // Paneli aç
            }

            StopAllCoroutines(); // AI oynuyorsa durdur
            isAIPlaying = false;
        }
    }
}