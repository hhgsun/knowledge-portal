import { useState } from "react";
import { createPortal } from "react-dom";
import { X, FileInput, Search, Sparkles, Layers, ArrowDown } from "lucide-react";
import { cn } from "../../lib/utils";

interface SearchFlowModalProps {
  open: boolean;
  onClose: () => void;
}

type FlowId = "indexing" | "fulltext" | "semantic" | "hybrid";

interface Step {
  title: string;
  detail: string;
  /** Config keys / components involved, shown as chips under the step. */
  refs?: string[];
  /** Rendered as a branch rather than a linear step. */
  branch?: boolean;
}

interface Flow {
  id: FlowId;
  label: string;
  icon: React.ReactNode;
  summary: string;
  steps: Step[];
}

// Kept deliberately close to the code: every number here is a real default from appsettings.json
// and every name is a real type. If the pipeline changes, this is the second place to update.
const FLOWS: Flow[] = [
  {
    id: "indexing",
    label: "İndeksleme",
    icon: <FileInput size={15} />,
    summary:
      "Bir makale yayınlandığında iki ayrı indeks kurulur: tam metin indeksi anında, vektör indeksi arka planda kuyrukla.",
    steps: [
      {
        title: "Makale kaydedilir",
        detail:
          "Makale 'published' duruma geçtiğinde iki yol aynı anda tetiklenir. Bunlar birbirinden bağımsızdır — biri gecikse de diğeri çalışır.",
        refs: ["ArticlesController"],
      },
      {
        title: "Tam metin indeksi anında güncellenir",
        detail:
          "Başlık, özet ve gövde+ek dosya metni ayrı ağırlıklarla (A/B/C) tek bir tsvector'e dönüşür. Türkçe kök bulma PostgreSQL'in snowball sözlüğünden, aksan katlama ise C# tarafındaki çeviri yazımdan gelir — ikisi de hem indeksleme hem sorgu anında aynı şekilde uygulanır.",
        refs: ["FullTextSearchService.SyncArticleAsync", "turkish snowball", "SlugHelper.Transliterate"],
      },
      {
        title: "Makale embedding kuyruğuna alınır",
        detail:
          "IndexedAt alanı null'a çekilir. Bu bir bayraktır: arka plan servisi yalnızca bu bayrağı taşıyan makaleleri toplar.",
        refs: ["IndexedAt = null"],
      },
      {
        title: "Arka plan servisi kuyruğu yoklar",
        detail:
          "Her turda en eski güncellenmiş makalelerden bir grup alınır. Sürekli hata veren makaleler artan bir bekleme süresiyle geri çekilir, böylece tek bir bozuk makale kuyruğu tıkamaz.",
        refs: ["EmbeddingBackgroundService", "BatchSize: 10", "PollingIntervalSeconds: 5", "EmbeddingFailureTracker"],
      },
      {
        title: "Metin çıkarılır ve parçalanır",
        detail:
          "Gövde metnine ek dosyalardan çıkarılan metin de eklenir (dosya başına 50.000 karakter sınırı). Sonuç 500 kelimelik, 50 kelime örtüşen parçalara bölünür; örtüşme, bir cümlenin parça sınırında ikiye bölünüp anlamını kaybetmesini engeller.",
        refs: ["AttachmentTextExtractor", "500 kelime / 50 örtüşme"],
      },
      {
        title: "Parça sayısı sınırlanır",
        detail:
          "Çok uzun bir doküman yüzlerce parça üretebilir ve arama penceresini tek başına doldurup diğer makaleleri sonuçlardan iter. Sınırın üstü indekslenmez — o metin tam metin aramasında bulunur, semantik aramada bulunmaz. Her kesme logda ayrıca uyarı olarak yazılır.",
        refs: ["MaxIndexChunksPerArticle: 100"],
      },
      {
        title: "Parçalar gruplar halinde vektöre çevrilir",
        detail:
          "Parçalar 16'lık gruplar halinde modele gönderilir. Tek istekte gönderilseydi isteğin süresi dokümanın boyuyla büyürdü ve en uzun dokümanlar zaman aşımına takılıp hiç indekslenemezdi.",
        refs: ["ChunkBatchSize: 16", "bge-m3", "1024 boyut"],
      },
      {
        title: "Tek işlemde yazılır",
        detail:
          "Parçalar ve 'indekslendi' işareti aynı transaction'da commit edilir. Makale bu sırada yayından kaldırılmışsa satır sürümü (xmin) değişmiş olur, işlem geri alınır ve yayında olmayan bir makale için parça yazılmaz.",
        refs: ["xmin guard", "pgvector HNSW"],
      },
      {
        title: "Filtre kolonları trigger'la doldurulur",
        detail:
          "Sahip, içerik tipi ve etiketler parça satırına kopyalanır. Bu kopyalar veritabanı trigger'larıyla güncel tutulur — uygulama kodunda tutulsaydı kaçırılan tek bir mutasyon yolu, hata vermeden bayat veriyle filtrelenmiş arama sonucu üretirdi.",
        refs: ["OwnerId", "ContentType", "TagSlugs"],
      },
    ],
  },
  {
    id: "fulltext",
    label: "Tam metin",
    icon: <Search size={15} />,
    summary:
      "Varsayılan arama türü. Tamamı tek bir SQL ifadesinde çalışır: eşleşme, filtre, sıralama, sayım ve sayfalama.",
    steps: [
      {
        title: "Sorgu ayrıştırılır",
        detail:
          "#etiket, @yazar ve +kategori:değer tokenları metinden ayrılıp filtreye dönüşür. Kategori ve değerler lookup tanımlarından dinamik çözülür; kalan kelimeler tsquery işaretlerinden temizlenir ve indeksleme tarafıyla aynı biçimde aksanları katlanır.",
        refs: ["SearchController", "TokenizeQuery"],
      },
      {
        title: "Önce kesinlik: tüm kelimeler",
        detail:
          "Kelimeler AND ile birleştirilir. Hiçbir şey eşleşmezse OR'a, o da eşleşmezse başlık/özet üzerinde ILIKE'a düşülür. Her basamak kendi içinde filtrelidir — bir basamağın eşleşmeleri filtre sonrası tükenirse arama bitmez, bir alt basamağa geçer.",
        refs: ["AND → OR → ILIKE"],
      },
      {
        title: "Filtreler aynı ifadenin içinde",
        detail:
          "Yazar, etiket, içerik tipi ve API anahtarı filtreleri tsquery eşleşmesiyle aynı WHERE'de yer alır. Aday kümesini kesip filtreyi sonradan uygulamak, yaygın bir terim korpusun büyük kısmıyla eşleştiğinde gerçek eşleşmeleri gizler.",
        refs: ["ArticleFilterSql"],
      },
      {
        title: "Gerçek toplam sayılır",
        detail:
          "Filtre sonrası eşleşme sayısı tam olarak hesaplanır. Sayfalama ve toplam sonuç sayısı buna dayanır; aday tavanı olmadığı için derin sayfalara da erişilebilir.",
        refs: ["COUNT(*)"],
      },
      {
        title: "Sıralanır ve sayfalanır",
        detail:
          "ts_rank_cd ile alaka sıralaması yapılır, eşitlikte Id'ye göre kararlı sıralama uygulanır — bu olmadan iki sayfa aynı kaydı gösterebilirdi.",
        refs: ["ts_rank_cd", "GIN index"],
      },
    ],
  },
  {
    id: "semantic",
    label: "Semantik",
    icon: <Sparkles size={15} />,
    summary:
      "Anlam benzerliğine göre arama. Sorgu da makaleler gibi vektöre çevrilir ve en yakın komşular aranır.",
    steps: [
      {
        title: "Sorgu vektöre çevrilir",
        detail:
          "Aramanın metni, makaleleri indekslerken kullanılan modelin aynısına gönderilir. Aynı model olmazsa vektörler karşılaştırılabilir olmaz.",
        refs: ["bge-m3", "1024 boyut"],
      },
      {
        title: "Aday penceresi belirlenir",
        detail:
          "Tarama makale değil PARÇA döndürür ve parça sayısı makaleden makaleye çok değişir. Bu yüzden istenen sonuç sayısının katı kadar parça çekilir; aksi halde tek bir uzun doküman pencereyi doldurup 20 sonuç isterken 3 makale dönmesine yol açar.",
        refs: ["VectorCandidateMultiplier: 30", "VectorCandidateMax: 2000"],
      },
      {
        title: "HNSW indeksi taranır",
        detail:
          "ef_search, aday listesinin büyüklüğüdür ve asla istenen satır sayısının altına düşmez — düşerse indeks zaten istenen kadar aday döndüremez. Filtre varsa pgvector, pencere dolana kadar grafiği gezmeye devam eder.",
        refs: ["hnsw.ef_search", "iterative_scan: relaxed_order"],
      },
      {
        title: "Filtreler taramanın içinde",
        detail:
          "Filtreler parça satırının kendi kolonlarına uygulanır, articles tablosuna join yapılmaz. Join olsaydı planlayıcı önce birleştirip sonra sıralamayı seçebilir ve HNSW indeksini tamamen devre dışı bırakabilirdi.",
        refs: ["denormalize kolonlar"],
      },
      {
        title: "Makaleye indirgenir",
        detail:
          "Her makalenin en yakın parçası SQL içinde seçilir, ardından benzerlik eşiği ve sonuç sınırı uygulanır. Sıra bu olduğu için 'limit' gerçekten makale sayar, parça değil.",
        refs: ["DISTINCT ON", "MinSimilarityScore: 0.5"],
      },
    ],
  },
  {
    id: "hybrid",
    label: "Hybrid",
    icon: <Layers size={15} />,
    summary:
      "Tam metin ve semantik aramayı birlikte çalıştırıp sonuçları sıralama bazlı birleştirir.",
    steps: [
      {
        title: "İki arama paralel çalışır",
        detail:
          "Tam metin ve semantik yollar birbirinden bağımsız olarak aday listesi üretir. Semantik taraf erişilemezse arama tam metinle devam eder ve yanıtta bunu belirten bir uyarı döner.",
        refs: ["candidateLimit = limit × 3, en fazla 50"],
      },
      {
        title: "Sıralama bazlı birleştirme (RRF)",
        detail:
          "Her aday, iki listedeki SIRASINA göre puan alır — puanlarına göre değil. Bunun sebebi ts_rank ile kosinüs benzerliğinin ölçek olarak karşılaştırılamaz olmasıdır; sıra ise karşılaştırılabilir.",
        refs: ["score = α / (k + sıra + 1)", "k = 60"],
      },
      {
        title: "Ağırlıklar",
        detail:
          "Semantik tarafa biraz daha fazla ağırlık verilir. İki listede birden geçen bir aday iki puanı da toplar ve doğal olarak yukarı çıkar; sonuçta 'fulltext', 'semantic' veya 'both' etiketiyle döner.",
        refs: ["α tam metin = 0.4", "α semantik = 0.6"],
      },
    ],
  },
];

export function SearchFlowModal({ open, onClose }: SearchFlowModalProps) {
  const [active, setActive] = useState<FlowId>("indexing");
  if (!open) return null;

  const flow = FLOWS.find((f) => f.id === active)!;

  // Rendered into document.body rather than in place: the page container uses space-y, which
  // would give the element after this one a margin it does not have while the modal is closed,
  // nudging the whole page down on open. A portal also keeps the overlay viewport-sized
  // regardless of what the surrounding layout does.
  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
      onClick={onClose}
    >
      <div
        // overflow-hidden so the scroll area cannot paint over the rounded bottom corners
        className="bg-white dark:bg-zinc-900 rounded-xl shadow-xl w-full max-w-3xl max-h-[88vh] flex flex-col overflow-hidden"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between px-6 py-4 border-b border-zinc-200 dark:border-zinc-700">
          <div>
            <h2 className="text-lg font-bold text-zinc-900 dark:text-zinc-100">
              Arama altyapısı nasıl çalışıyor?
            </h2>
            <p className="text-sm text-zinc-500 dark:text-zinc-400 mt-0.5">
              Her adımdaki değerler yapılandırmadaki gerçek varsayılanlardır
            </p>
          </div>
          <button
            onClick={onClose}
            className="p-1 rounded-lg text-zinc-400 hover:text-zinc-600 hover:bg-zinc-100 dark:hover:bg-zinc-800 transition-colors"
            aria-label="Kapat"
          >
            <X size={20} />
          </button>
        </div>

        <div className="flex gap-1 px-6 pt-4 flex-wrap">
          {FLOWS.map((f) => (
            <button
              key={f.id}
              onClick={() => setActive(f.id)}
              className={cn(
                "flex items-center gap-1.5 px-3 py-1.5 text-sm rounded-lg transition-colors",
                active === f.id
                  ? "bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300"
                  : "text-zinc-600 dark:text-zinc-400 hover:bg-zinc-100 dark:hover:bg-zinc-800"
              )}
            >
              {f.icon}
              {f.label}
            </button>
          ))}
        </div>

        {/* flex-1 makes this the region that absorbs the leftover height; min-h-0 lets it
            shrink below its content so it actually scrolls instead of pushing the panel open
            and spilling past its bottom edge. */}
        <div className="flex-1 min-h-0 px-6 py-4 overflow-y-auto">
          <p className="text-sm text-zinc-600 dark:text-zinc-300 mb-5 leading-relaxed">
            {flow.summary}
          </p>

          <ol className="space-y-1">
            {flow.steps.map((step, i) => (
              <li key={step.title}>
                <div
                  className={cn(
                    "flex gap-3 rounded-lg p-3",
                    step.branch && "bg-amber-50/60 dark:bg-amber-900/10"
                  )}
                >
                  <div
                    className={cn(
                      "shrink-0 w-6 h-6 rounded-full flex items-center justify-center text-xs font-semibold",
                      step.branch
                        ? "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300"
                        : "bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-300"
                    )}
                  >
                    {i + 1}
                  </div>
                  <div className="min-w-0">
                    <h3 className="text-sm font-semibold text-zinc-900 dark:text-zinc-100">
                      {step.title}
                    </h3>
                    <p className="text-sm text-zinc-600 dark:text-zinc-400 mt-1 leading-relaxed">
                      {step.detail}
                    </p>
                    {step.refs && (
                      <div className="flex flex-wrap gap-1.5 mt-2">
                        {step.refs.map((r) => (
                          <span
                            key={r}
                            className="text-xs font-mono px-1.5 py-0.5 rounded bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400"
                          >
                            {r}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
                {i < flow.steps.length - 1 && (
                  <div className="flex justify-center py-0.5">
                    <ArrowDown size={12} className="text-zinc-300 dark:text-zinc-600" />
                  </div>
                )}
              </li>
            ))}
          </ol>
        </div>
      </div>
    </div>,
    document.body
  );
}
