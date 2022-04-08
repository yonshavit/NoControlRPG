using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using System.Linq;
using System;
using Assets.Scripts.UnityBase;
using Assets.Scripts.Base;

public class BattleMainManagerScript : MonoBehaviour {
  private SpawnPool spawnPool;
  private GameObject player;
  private readonly Dictionary<Guid, EnemyUnitScript> enemies = new();
  private readonly Dictionary<Guid, BulletScript> bullets = new();
  private readonly Dictionary<Guid, BeamScript> beams = new();
  private const float TIME_BETWEEN_SPAWNS = 1;
  private float timeToNextSpawn = TIME_BETWEEN_SPAWNS;
  private enum Mode { Battle, Inventory }
  private Mode mode;

  private UIManagerScript uiManager;
  private float roundStartTime;

  private readonly HashSet<BulletScript> bulletsToRelease = new();
  private readonly HashSet<BeamScript> beamsToRelease = new();

  private static EnemyConfig MakeScoutMech(WeaponConfig weaponConfig) {
    return new EnemyConfig(health: new LevelBasedValue(constant: 2f, linearCoefficient: 1),
                    imageName: "ScoutMech",
                    speed: LevelBasedValue.ConstantValue(15f),
                    weapon: weaponConfig,
                    dropChance: 0.07f);
  }


  private static EnemyConfig MakeHeavyMech(WeaponConfig weaponConfig) {
    return new EnemyConfig(health: new LevelBasedValue(constant: 4f, linearCoefficient: 1.5f),
                    imageName: "HeavyMech",
                    speed: LevelBasedValue.ConstantValue(10f),
                    weapon: weaponConfig,
                    dropChance: 0.15f);
  }

  private readonly List<EnemyConfig> enemyConfigs = new() {
    MakeScoutMech(WeaponConfig.TWO_SHOT_SHOTGUN),
    MakeScoutMech(WeaponConfig.FLAMER),
    MakeHeavyMech(WeaponConfig.RIFLE),
    MakeHeavyMech(WeaponConfig.MISSILE)
  };

  // Start is called before the first frame update
  protected void Start() {
    spawnPool = GetComponent<SpawnPool>();
    player = GameObject.Find("Player").gameObject;
    uiManager = GameObject.FindObjectOfType<UIManagerScript>();

    SwitchToInventory();
  }

  public void SwitchContext() {
    switch (mode) {
      case Mode.Battle:
        SwitchToInventory();
        break;
      case Mode.Inventory:
        SwitchToBattle();
        break;
    }
  }

  private void SwitchToBattle() {
    mode = Mode.Battle;
    uiManager.ToBattleMode();
    roundStartTime = Time.timeSinceLevelLoad;
  }

  private void SwitchToInventory() {
    ReleaseAllEntities();
    mode = Mode.Inventory;
    uiManager.ToInventoryMode();
  }

  private void HitPlayer(float damage) {
    var shield = Player.Instance.Shield;
    shield.TimeBeforeNextRecharge = shield.TimeBeforeRecharge;
    if (shield.CurrentStrength > 0) {
      shield.CurrentStrength = Mathf.Max(0f, shield.CurrentStrength - damage);
    } else {
      Player.Instance.CurrentHealth -= damage;
    }
  }

  private void ReleaseAllEntities() {
    bullets.Values.ForEach(bullet => bulletsToRelease.Add(bullet));
    beams.Values.ForEach(beam => beamsToRelease.Add(beam));
    // pretty ugly hack, but it works
    enemies.Values.ForEach(enemy => enemy.Health = 0);
  }

  internal void BulletHitEnemy(BulletScript shotScript, GameObject enemy) {
    bulletsToRelease.Add(shotScript);
    enemy.GetComponent<EnemyUnitScript>().Health -= shotScript.Weapon.DamagePerBullet;
  }

  internal void BulletHitPlayer(BulletScript shotScript, GameObject player) {
    bulletsToRelease.Add(shotScript);
    HitPlayer(shotScript.Weapon.DamagePerBullet);
  }

  internal void BeamHitPlayer(BeamScript beamScript, GameObject player) {
    HitPlayer(beamScript.Weapon.DamagePerSecond * Time.deltaTime);
  }

  internal void BeamHitEnemy(BeamScript beamScript, GameObject enemy) {
    enemy.GetComponent<EnemyUnitScript>().Health -= beamScript.Weapon.DamagePerSecond * Time.deltaTime;
  }

  private float LevelBasedOnTime() {
    // level increases by 1 every 10 seconds.
    return (Time.timeSinceLevelLoad - roundStartTime) / 10f;
  }

  private void SpawnEnemyIfNeeded() {
    const int TARGET_NUMBER_OF_ENEMIES = 50;
    if (enemies.Count >= TARGET_NUMBER_OF_ENEMIES) {
      return;
    }
    timeToNextSpawn -= Time.deltaTime;
    if (timeToNextSpawn > 0) {
      return;
    }
    var config = enemyConfigs.ChooseRandomValue();
    var newEnemy = spawnPool.GetUnit(config.ImageName);
    var verticalSize = Camera.main.orthographicSize;
    var horizontalSize = verticalSize * Screen.width / Screen.height;
    var distance = Mathf.Sqrt(Mathf.Pow(verticalSize, 2) + Mathf.Pow(horizontalSize, 2)) + 0.1f;
    newEnemy.transform.position = UnityEngine.Random.insideUnitCircle.normalized * distance;
    newEnemy.Init(config, LevelBasedOnTime());
    enemies[newEnemy.Identifier] = newEnemy;
    timeToNextSpawn = TIME_BETWEEN_SPAWNS;
  }

  private void MoveEnemies() {
    var playerPosition = player.transform.position;
    foreach (var enemy in enemies.Values) {
      enemy.transform.RotateTowards(playerPosition, enemy.Speed * Time.deltaTime);
      enemy.gameObject.MoveForwards(1.5f);
    }
  }

  private EnemyUnitScript FindEnemyInRange(float weaponRange) {
    return enemies.Values.FirstOrDefault(enemy => Vector3.Distance(enemy.transform.position, player.transform.position) < weaponRange);
  }

  private void AdjustBeamPosition(BeamScript beam, float rotationSpeed) {
    // If position isn't moved far from target before rotation, the rotation will go nuts.
    beam.transform.position = beam.Shooter.transform.position;
    beam.transform.RotateTowards(beam.Target.transform.position, rotationSpeed);
    beam.transform.position += beam.transform.right.normalized * beam.Weapon.Range / 2;
  }

  private void MoveShots() {
    foreach (var shot in bullets.Values) {
      // TODO - consider using RigidBody's movement function, instead of using kinematic rigidbodies.
      shot.gameObject.MoveForwards(shot.Speed);
      if (!shot.InRange()) {
        bulletsToRelease.Add(shot);
      }
    }

    foreach (var beam in beams.Values) {
      beam.Weapon.CurrentCharge -= Time.deltaTime;
      if (beam.Weapon.CurrentCharge <= 0f
        || !beam.Shooter.activeSelf
        || !beam.Target.activeSelf) { // TODO - this condition means that if an enemy is restored in the next frame after being destroyed, the beam will continue shooting.
        beamsToRelease.Add(beam);
      } else if (beam.Target.activeSelf) {
        AdjustBeamPosition(beam, 360);
      }
    }
  }

  private const float BEAM_SCALE = 0.1f;

  private void CreateBullet(GameObject shooter, BulletWeaponInstance weapon, Vector3 to) {
    var bullet = spawnPool.GetBullet(weapon.Config.shotImageName);
    bullets[bullet.Identifier] = bullet;
    bullet.transform.position = shooter.transform.position;
    bullet.Init(shooter, shooter.transform.position, weapon);
    bullet.transform.RotateTowards(to, 360, (float)Randomiser.NextDouble(-weapon.Config.ShotSpreadInDegrees, weapon.Config.ShotSpreadInDegrees));
  }

  private void ShootBulletsSalvo(GameObject shooter, BulletWeaponInstance weapon, Vector3 to) {
    for (int i = 0; i < weapon.Config.numberOfBulletsPerSalvo; ++i) {
      CreateBullet(shooter, weapon, to);
    }
  }

  private IEnumerator ShootBulletsSalvos(GameObject shooter, BulletWeaponInstance weapon, Vector3 to) {
    weapon.CurrentCharge = 0;
    int salvoCount = weapon.Config.numberOfSalvosPerShot;
    while (true) {
      ShootBulletsSalvo(shooter, weapon, to);
      if (--salvoCount > 0) {
        yield return new WaitForSeconds(weapon.Config.TimeBetweenSalvosInSeconds);
      } else {
        yield break;
      }
    }
  }

  private void CreateBeam(GameObject shooter, BeamInstance weapon, GameObject target) {
    var beam = spawnPool.GetBeam(weapon.Config.shotImageName);
    beams[beam.Identifier] = beam;
    beam.transform.position = shooter.transform.position;
    beam.Init(shooter, weapon, target);
    beam.transform.localScale = new Vector3(weapon.Range * BEAM_SCALE, BEAM_SCALE, BEAM_SCALE);
    AdjustBeamPosition(beam, 360);
    weapon.IsCurrentlyfiring = true;
  }

  private void CreateShot(GameObject shooter, WeaponBase weapon, GameObject target) {
    if (weapon is BeamInstance beam) {
      CreateBeam(shooter, beam, target);
    } else if (weapon is BulletWeaponInstance bullet) {
      StartCoroutine(ShootBulletsSalvos(shooter, bullet, target.transform.position));
    }
  }

  private void ShootWeapon(GameObject shooter, WeaponBase weapon, GameObject target) {
    CreateShot(shooter, weapon, target);
  }

  private void TryShootWeapon(WeaponBase weapon) {
    if (!weapon.CanShoot()) {
      return;
    }
    var enemyInRange = FindEnemyInRange(weapon.Range);
    if (enemyInRange == null) {
      return;
    }

    ShootWeapon(player, weapon, enemyInRange.gameObject);
  }

  private void ShootEnemies() {
    TryShootWeapon(Player.Instance.Weapon1);
    TryShootWeapon(Player.Instance.Weapon2);
  }

  private void ShootPlayer() {
    foreach (var enemy in enemies.Values) {
      var weapon = enemy.Weapon;
      weapon.CurrentCharge = Mathf.Min(weapon.MaxCharge, weapon.CurrentCharge + Time.deltaTime);
      if (!weapon.CanShoot() || Vector3.Distance(enemy.transform.position, player.transform.position) > weapon.Range) {
        continue;
      }
      ShootWeapon(enemy.gameObject, weapon, player);
    }
  }

  // Update is called once per frame
  protected void Update() {
    ReleaseEntities();

    if (mode != Mode.Battle) {
      return;
    }

    if (Player.Instance.CurrentHealth <= 0) {
      SwitchToInventory();
    }

    SpawnEnemyIfNeeded();
    MoveEnemies();
    ShootEnemies();
    ShootPlayer();
    MoveShots();
    RechargeSystems();
    uiManager.UpdateUIOverlay();
  }

  private void RechargeSystems() {
    var player = Player.Instance;
    var reactor = player.Reactor;
    var baselineEnergyRequirements = player.Shield.BaselineEnergyRequirement +
      player.TargetingSystem.BaselineEnergyRequirement +
      player.Weapon1?.BaselineEnergyRequirement ?? 0f +
      player.Weapon2?.BaselineEnergyRequirement ?? 0f;
    var availableEnergy = reactor.CurrentEnergyLevel + (reactor.EnergyRecoveryPerSecond - baselineEnergyRequirements) * Time.deltaTime;
    var chargeRequirementPerSecond = 0f;

    chargeRequirementPerSecond += player.Shield.CurrentChargingRequirementPerSecond;
    chargeRequirementPerSecond += player.Weapon1?.CurrentChargingRequirementPerSecond ?? 0;
    chargeRequirementPerSecond += player.Weapon2?.CurrentChargingRequirementPerSecond ?? 0;
    var currentFrameRequirement = chargeRequirementPerSecond * Time.deltaTime;

    var ratio = Mathf.Min(availableEnergy / currentFrameRequirement, 1);
    player.Shield.Charge(ratio);
    player.Weapon1?.Charge(ratio);
    player.Weapon2?.Charge(ratio);
    reactor.CurrentEnergyLevel = Math.Min(player.Reactor.MaxEnergyLevel, Mathf.Max(0, availableEnergy - currentFrameRequirement));
  }

  private void ReleaseEntities() {
    foreach (var bullet in bulletsToRelease) {
      bullets.Remove(bullet.Identifier);
      spawnPool.ReturnBullet(bullet);
    }
    bulletsToRelease.Clear();

    var enemiesToRelease = enemies.Where(pair => pair.Value.Health <= 0).ToList();
    foreach (var (key, enemy) in enemiesToRelease) {
      spawnPool.SpawnUnitExplosion(enemy.transform.position);
      enemies.Remove(key);
      spawnPool.ReturnUnit(enemy);
    }
    enemiesToRelease.Clear();

    foreach (var beam in beamsToRelease) {
      beams.Remove(beam.Identifier);
      spawnPool.ReturnBeam(beam);
      beam.Weapon.IsCurrentlyfiring = false;
    }
    beamsToRelease.Clear();
  }
}
