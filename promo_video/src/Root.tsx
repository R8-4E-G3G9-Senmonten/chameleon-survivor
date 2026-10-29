import {Composition, Still} from 'remotion';
import {LOGO_H, LOGO_W, TitleLogo} from './Logo';
import {Promo, PROMO_FRAMES} from './Promo';
import {DIAGRAM_H, DIAGRAM_W, SIMPLE_H, SIMPLE_W, SystemDiagram, SystemDiagramSimple} from './SystemDiagram';
import {SCENE_H, SCENE_W, SystemScene} from './SystemScene';
import {Overview, OVERVIEW_H, OVERVIEW_W} from './Overview';
import {MINIMAL_H, MINIMAL_W, SystemMinimal} from './SystemMinimal';
import {SlideBackground, SLIDE_H, SLIDE_W} from './SlideBackgrounds';
import {IconOnly, ICON_SIZE, SlideTech, SLIDE_TECH_H, SLIDE_TECH_W, SlideSystem, SlideSystemIPO, SlideSystemSimple, SLIDE_SIMPLE_H, SLIDE_SIMPLE_W, SLIDE_IPO_H, SLIDE_IPO_W, SLIDE_SYS_H, SLIDE_SYS_W} from './SlideSystem';

export const RemotionRoot: React.FC = () => (
  <>
    <Composition id="Promo" component={Promo} durationInFrames={PROMO_FRAMES} fps={30} width={1920} height={1080} />
    <Still id="TitleLogo" component={TitleLogo} width={LOGO_W} height={LOGO_H} />
    <Still id="SystemDiagram" component={SystemDiagram} width={DIAGRAM_W} height={DIAGRAM_H} />
    <Still id="SystemDiagramSimple" component={SystemDiagramSimple} width={SIMPLE_W} height={SIMPLE_H} />
    <Still id="SystemScene" component={SystemScene} width={SCENE_W} height={SCENE_H} />
    <Still id="Overview" component={Overview} width={OVERVIEW_W} height={OVERVIEW_H} />
    <Still id="SystemMinimal" component={SystemMinimal} width={MINIMAL_W} height={MINIMAL_H} />
    <Still id="SystemMinimal5G" component={SystemMinimal} width={MINIMAL_W} height={MINIMAL_H} defaultProps={{phoneLink: '5G(インターネット経由)'}} />

    <Still id="Slide-light-title" component={SlideBackground} width={SLIDE_W} height={SLIDE_H} defaultProps={{theme: 'light', kind: 'title'}} />
    <Still id="Slide-light-title-nologo" component={SlideBackground} width={SLIDE_W} height={SLIDE_H} defaultProps={{theme: 'light', kind: 'title', logo: false}} />
    <Still id="Slide-light-content" component={SlideBackground} width={SLIDE_W} height={SLIDE_H} defaultProps={{theme: 'light', kind: 'content'}} />
    <Still id="Slide-dark-title" component={SlideBackground} width={SLIDE_W} height={SLIDE_H} defaultProps={{theme: 'dark', kind: 'title'}} />
    <Still id="Slide-dark-content" component={SlideBackground} width={SLIDE_W} height={SLIDE_H} defaultProps={{theme: 'dark', kind: 'content'}} />
    <Still id="SlideSystem-flow" component={SlideSystem} width={SLIDE_SYS_W} height={SLIDE_SYS_H} defaultProps={{variant: 'flow'}} />
    <Still id="SlideSystem-green" component={SlideSystem} width={SLIDE_SYS_W} height={SLIDE_SYS_H} defaultProps={{variant: 'green'}} />
    <Still id="SlideSystem-ipo" component={SlideSystemIPO} width={SLIDE_IPO_W} height={SLIDE_IPO_H} />
    <Still id="SlideSystem-simple" component={SlideSystemSimple} width={SLIDE_SIMPLE_W} height={SLIDE_SIMPLE_H} />
    <Still id="Icon-mat" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'mat'}} />
    <Still id="Icon-esp32" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'esp32'}} />
    <Still id="Icon-phone" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'phone'}} />
    <Still id="Icon-monitor" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'monitor'}} />
    <Still id="Icon-laptop" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'laptop'}} />
    <Still id="Icon-cloudflare" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'cloudflare'}} />
    <Still id="Icon-unity" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'unity'}} />
    <Still id="SlideTech" component={SlideTech} width={SLIDE_TECH_W} height={SLIDE_TECH_H} />
    <Still id="Icon-unityMark" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'unityMark'}} />
    <Still id="Icon-server" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'server'}} />
    <Still id="Icon-browser" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'browser'}} />
    <Still id="Icon-ai3d" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'ai3d'}} />
    <Still id="Icon-tree" component={IconOnly} width={ICON_SIZE} height={ICON_SIZE} defaultProps={{name: 'tree'}} />
  </>
);
