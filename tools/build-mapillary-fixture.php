<?php
declare(strict_types=1);

// Local sample only: shared Folio builder, no Harvest registry or workflow mutations.
// Usage: php Tools~/build-mapillary-fixture.php HARVEST_ROOT CAPTURE_DIRECTORY [LIMIT]
use Doctrine\DBAL\DriverManager;
use Doctrine\ORM\EntityManager;
use Doctrine\ORM\Mapping\UnderscoreNamingStrategy;
use Doctrine\ORM\ORMSetup;
use Survos\DataContracts\Path\DataPaths;
use Survos\DataContracts\Metadata\ContentType;
use Survos\DataContracts\Vocabulary\Core;
use Survos\DataContracts\Vocabulary\ItemField;
use Survos\DataContracts\Vocabulary\Panorama;
use Survos\FolioBundle\DBAL\FolioConnectionWrapper;
use Survos\FolioBundle\Dto\FolioBuildInput;
use Survos\FolioBundle\Service\{FolioArchivePreparer,FolioArchiveService,FolioBuilder,FolioDocsBuilder,FolioDtoTypeResolver,FolioFtsIndexer,FolioIngestService,FolioSchemaManager,FolioSchemaSnapshotter,FolioService,FolioSummaryService,FolioViewBuilder};
use Survos\JsonlBundle\Service\{JsonlCountService,JsonlStateService};
use Survos\Mapillary\{MapillaryClient,SerializerFactory};
use Symfony\Component\Cache\Adapter\ArrayAdapter;
use Symfony\Component\Dotenv\Dotenv;
use Symfony\Component\HttpClient\HttpClient;

if ($argc < 3 || $argc > 4) {
    fwrite(STDERR, "Usage: php build-mapillary-fixture.php HARVEST_ROOT CAPTURE_DIRECTORY [LIMIT]\n");
    exit(2);
}
$harvest = realpath($argv[1]);
$capture = realpath($argv[2]);
$limit = $argc === 4 ? filter_var($argv[3], FILTER_VALIDATE_INT) : 10;
if (!$harvest || !$capture || $limit < 1 || $limit > 10) {
    throw new RuntimeException('Existing paths and a limit of 1–10 are required.');
}
require $harvest.'/vendor/autoload.php';
$root = dirname(__DIR__).'/Data/mapillary-smoke';
@mkdir($root.'/media', 0777, true);
@mkdir($root.'/tmp', 0777, true);
putenv('TMPDIR='.$root.'/tmp');
$manifest = json_decode(file_get_contents($capture.'/acquisition.json'), true, flags: JSON_THROW_ON_ERROR);
$source = $capture.'/images.jsonl';
if ($manifest['datasetKey'] !== 'mapillary/hu' || !hash_equals($manifest['imagesSha256'], hash_file('sha256', $source))) {
    throw new RuntimeException('Budapest capture identity/checksum mismatch.');
}
$all = array_map(fn ($line) => json_decode($line, true, flags: JSON_THROW_ON_ERROR), file($source, FILE_IGNORE_NEW_LINES | FILE_SKIP_EMPTY_LINES));
if (count($all) !== $manifest['sampleCount'] || count(array_unique(array_column($all, 'id'))) !== count($all)) {
    throw new RuntimeException('Capture count/identity mismatch.');
}
$rows = array_slice($all, 0, $limit);
foreach ($rows as $row) {
    if ($row['is_pano'] !== true || $row['width'] !== 2 * $row['height'] || $row[Panorama::MEDIA_KIND] !== Panorama::EQUIRECTANGULAR
        || empty($row['creator']['username']) || !isset($row['computed_geometry']['coordinates'])) {
        throw new RuntimeException('Expected an attributable 2:1 panorama with computed coordinates.');
    }
}
$id = $rows[0][ItemField::ID];
$imagePath = $root.'/media/'.$id.'.jpg';
// A durable local file prevents expiring signed CDN links entering the Folio.
if (!is_file($imagePath)) {
    (new Dotenv())->loadEnv($harvest.'/.env');
    $token = $_ENV['MAPILLARY_ACCESS_TOKEN'] ?? throw new RuntimeException('MAPILLARY_ACCESS_TOKEN missing.');
    $client = new MapillaryClient(HttpClient::create(), SerializerFactory::create(), $token);
    $image = $client->getImage($id);
    if (!$image->isPano || $image->width !== 2 * $image->height || $image->thumbOriginalUrl === null) {
        throw new RuntimeException('Refreshed image is not a full panorama.');
    }
    try {
        $response = HttpClient::create()->request('GET', $image->thumbOriginalUrl, ['timeout' => 60, 'max_duration' => 120]);
        if ($response->getStatusCode() !== 200) throw new RuntimeException('Image download failed.');
        $bytes = $response->getContent();
        $size = getimagesizefromstring($bytes);
        if (!$size || $size[0] !== 2 * $size[1] || $size[2] !== IMAGETYPE_JPEG) throw new RuntimeException('Invalid panorama JPEG.');
        file_put_contents($imagePath.'.tmp', $bytes);
        rename($imagePath.'.tmp', $imagePath);
    } catch (Throwable) {
        // Do not expose signed CDN URLs in exception diagnostics.
        throw new RuntimeException('Panorama download/validation failed; signed URL omitted.');
    }
}
$size = getimagesize($imagePath);
if (!$size || $size[0] !== 2 * $size[1]) throw new RuntimeException('Cached image is not 2:1.');
$normalized = [];
foreach ($rows as $row) {
    $coordinates = $row['computed_geometry']['coordinates'];
    $record = [
        ItemField::ID => $row[ItemField::ID], ItemField::CONTENT_TYPE => ContentType::PHOTOGRAPH,
        ItemField::TITLE => 'Budapest — Mapillary '.$row[ItemField::ID],
        ItemField::CREATOR => [$row['creator']['username']],
        ItemField::CITATION_URL => $row[ItemField::CITATION_URL],
        ItemField::SOURCE => 'Mapillary', ItemField::DATE => gmdate('Y-m-d\TH:i:s\Z', intdiv($row['captured_at'], 1000)),
        ItemField::LATITUDE => $coordinates[1], ItemField::LONGITUDE => $coordinates[0],
        Panorama::MEDIA_KIND => Panorama::EQUIRECTANGULAR,
        'width' => $row['width'], 'height' => $row['height'],
        'mapillary' => $row,
    ];
    if ($row[ItemField::ID] === $id) $record[ItemField::DISPLAY_IMAGE_URL] = 'media/'.$id.'.jpg';
    $normalized[] = json_encode($record, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES);
}
$inputPath = $root.'/obj.jsonl';
file_put_contents($inputPath, implode("\n", $normalized)."\n");
$config = ORMSetup::createAttributeMetadataConfiguration([dirname((new ReflectionClass(\Survos\FolioBundle\Entity\Folio::class))->getFileName())], true);
$config->enableNativeLazyObjects(true);
$config->setNamingStrategy(new UnderscoreNamingStrategy());
$em = new EntityManager(DriverManager::getConnection(['driver' => 'pdo_sqlite', 'path' => $root.'/unused.db', 'wrapperClass' => FolioConnectionWrapper::class]), $config);
$folios = new FolioService($em, new FolioSchemaManager(new ArrayAdapter()), new DataPaths($root));
$state = new JsonlStateService();
$resolver = new FolioDtoTypeResolver();
$views = new FolioViewBuilder();
$preparer = new FolioArchivePreparer(new FolioSchemaSnapshotter($resolver), $views, new FolioDocsBuilder());
$builder = new FolioBuilder(new FolioIngestService($folios, $resolver, new FolioSummaryService(), new JsonlCountService($state)), $folios,
    new FolioArchiveService($folios, new FolioFtsIndexer(), $preparer, $views), $preparer, $state);
try {
    $result = $builder->build(new FolioBuildInput(code: 'mapillary-smoke/hu', sources: [Core::OBJECT => $inputPath],
        metadata: ['label' => 'Budapest panorama smoke fixture', 'coverageComplete' => false], sourceLocale: 'en'));
} finally {
    $folios->closeActive();
    $state->closeAll();
}
copy($result['path'], $root.'/budapest.folio');
$evidence = ['folio' => $root.'/budapest.folio', 'rows' => $result['rows'], 'imageId' => $id,
    'image' => $imagePath, 'imageWidth' => $size[0], 'imageHeight' => $size[1],
    'imageSha256' => hash_file('sha256', $imagePath), 'captureSha256' => $manifest['imagesSha256'],
    'creator' => $rows[0]['creator']['username'], 'sourceUrl' => $rows[0][ItemField::CITATION_URL],
    'scope' => 'Local fixture; not registered or published by Harvest'];
file_put_contents($root.'/evidence.json', json_encode($evidence, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES)."\n");
echo json_encode($evidence, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES)."\n";
