'use strict';
/* COMBAT-V4 §2.7 throw-away estimate of army crits: monkey-patches BattleModel.prototype.attack IN MEMORY (game files untouched),
   runs 300 AI-vs-AI tactical battles with crits off / on (one random draw per attack either way). Usage: node design-v4/combat/crit-estimate.js */
// scratch estimate of army crits (does not modify game code): monkeypatch BattleModel.prototype.attack
const path=require('path'),fs=require('fs'),vm=require('vm');global.window=global;
const root=path.join(__dirname,'..','..','js');
for (const f of ['core.js','data.js','model.js','strategy-ai.js','battle-model.js','specials-data.js','specials.js','tactics-data.js']) vm.runInThisContext(fs.readFileSync(path.join(root,f),'utf8'),{filename:f});
const SG=global.SG, {GameState,Conquest,BattleModel}=SG, M=SG.M, Balance=SG.Balance;
const FORM=[-0.02,0,0.02,0.01,0.03,0.04,0.01,0];
let CRIT=false; const st={atk:0,crit:0,byWar:{},dmg:0,critDmg:0};
const P=BattleModel.prototype;
P.critChance=function(u){ const g=u.gen, L=(SG.Progress&&SG.Progress.enabled)?(g.level||1):1; return M.clamp(0.02+0.0015*Math.max(0,g.war-50)+0.004*(L-1)+FORM[u.formation],0,0.25); };
P.attack=function(a,t){
  const r={dmg:0,counter:0,targetRouted:false,selfRouted:false,crit:false};
  const roll=SG.Random.value();                      // always consume one roll
  const crit=CRIT && roll<this.critChance(a);
  let d=this.atkPower(a)*Balance.DamageK/this.defFactor(t)*SG.Random.rangeFloat(0.85,1.15);
  if(crit) d*=1.5;
  r.dmg=Math.max(10,M.roundToInt(d)); r.dmg=Math.min(r.dmg,t.troops); t.troops-=r.dmg; r.crit=crit;
  st.atk++; st.dmg+=r.dmg; if(crit){st.crit++; st.critDmg+=r.dmg/3;}
  const wb=Math.floor(a.gen.war/10)*10; const b=st.byWar[wb]=st.byWar[wb]||[0,0]; b[0]++; if(crit) b[1]++;
  t.morale=Math.max(0,t.morale-M.roundToInt(r.dmg*40/Math.max(200,t.troops+r.dmg))-(crit?5:0));
  r.targetRouted=this.checkRout(t);
  if(!r.targetRouted){ r.counter=Math.max(5,M.roundToInt(this.atkPower(t)*Balance.DamageK*Balance.CounterRatio*this.modK(t,'counter')/this.defFactor(a)*SG.Random.rangeFloat(0.85,1.15)));
    r.counter=Math.min(r.counter,a.troops); a.troops-=r.counter; a.morale=Math.max(0,a.morale-M.roundToInt(r.counter*30/Math.max(200,a.troops+r.counter))); r.selfRouted=this.checkRout(a); }
  a.morale=Math.min(100,a.morale+(r.dmg>r.counter?3:0)); this.xpHit(a,t,r.dmg); if(r.counter>0) this.xpHit(t,a,r.counter); this.checkEnd(); return r; };
const pairs=[['chenliu','luoyang'],['pingyuan','nanpi'],['changsha','jiangling'],['beiping','nanpi'],['wan','xiangyang'],['xiapi','xiaopei'],['changan','tianshui'],['chengdu','jiangzhou'],['ye','jinyang'],['shouchun','lujiang']];
function run(seed,pr){ SG.Random.seed(seed*7); const g=SG.G=GameState.newGame('cao'); const src=g.cityByKey(pr[0]),tgt=g.cityByKey(pr[1]);
  const atk=g.officersIn(src).filter(x=>x.troops>0).slice(0,5); const s=Conquest.prepare(src.owner,src,tgt,atk,3000,0); const Mdl=new BattleModel(s);
  let guard=0; while(Mdl.result===0&&guard++<2000){ for(let pass=0;pass<4&&Mdl.result===0;pass++){ const order=Mdl.alive(Mdl.side).filter(u=>Mdl.canAct(u)); if(!order.length) break;
    for(const u of order){ if(!Mdl.canAct(u)||Mdl.result!==0) continue; const plan=Mdl.planFor(u); if(plan.move) Mdl.move(u,plan.move); if(Mdl.result!==0) break; const act=Mdl.planAction(u);
      if(act.kind==='attack') Mdl.attack(u,act.target); else if(act.kind==='tactic') Mdl.useTactic(u,act.tactic,act.target); else if(act.kind==='duel') Mdl.duel(u,act.target); else if(act.kind==='special') Mdl.useSpecial(u,act.target);
      Mdl.spend(u); } } if(Mdl.result===0) Mdl.endSide(); }
  return Mdl; }
for(const on of [false,true]){ CRIT=on; Object.assign(st,{atk:0,crit:0,byWar:{},dmg:0,critDmg:0}); let w=0,days=0,n=0,to=0;
  for(let seed=1;seed<=300;seed++){ const Mdl=run(1000+seed,pairs[seed%pairs.length]); n++; days+=Mdl.day; if(Mdl.result===1) w++; if((Mdl.resultReason||'').indexOf('期限')>=0) to++; }
  console.log(`crit ${on?'ON ':'OFF'}: atk win ${(100*w/n).toFixed(1)}% days ${(days/n).toFixed(2)} timeouts ${to} attacks ${st.atk} crit rate ${(100*st.crit/Math.max(1,st.atk)).toFixed(1)}% avg dmg ${(st.dmg/st.atk).toFixed(0)} crit extra dmg share ${(100*st.critDmg/st.dmg).toFixed(1)}%`);
  if(on) console.log('  crit rate by war decade: '+Object.keys(st.byWar).sort().map(k=>k+':'+(100*st.byWar[k][1]/st.byWar[k][0]).toFixed(1)+'%(n'+st.byWar[k][0]+')').join(' ')); }
