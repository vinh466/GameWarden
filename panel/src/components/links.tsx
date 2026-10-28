import { forwardRef, type ComponentPropsWithoutRef } from 'react';
import { createLink, type LinkComponent } from '@tanstack/react-router';
import {
  ActionIcon,
  Anchor,
  Button,
  Card,
  NavLink,
  type ActionIconProps,
  type AnchorProps,
  type ButtonProps,
  type CardProps,
  type NavLinkProps,
} from '@mantine/core';

// Mantine components as type-safe router links: TanStack Router's createLink pattern, from its "Custom Link" guide
// (Mantine example). `to` and `params` are checked against the route tree, unlike `component={Link}`.

/** Mantine's component props plus the anchor's own attributes (onClick, target, …); the router supplies href. */
type AsAnchor<P> = Omit<P & ComponentPropsWithoutRef<'a'>, 'href'>;

const AnchorWrapper = forwardRef<HTMLAnchorElement, AsAnchor<AnchorProps>>((props, ref) => (
  <Anchor ref={ref} {...props} />
));
const RouterAnchor = createLink(AnchorWrapper);
export const AnchorLink: LinkComponent<typeof AnchorWrapper> = (props) => (
  <RouterAnchor preload="intent" {...props} />
);

const ButtonWrapper = forwardRef<HTMLAnchorElement, AsAnchor<ButtonProps>>((props, ref) => (
  <Button component="a" ref={ref} {...props} />
));
const RouterButton = createLink(ButtonWrapper);
export const ButtonLink: LinkComponent<typeof ButtonWrapper> = (props) => (
  <RouterButton preload="intent" {...props} />
);

const ActionIconWrapper = forwardRef<HTMLAnchorElement, AsAnchor<ActionIconProps>>((props, ref) => (
  <ActionIcon component="a" ref={ref} {...props} />
));
const RouterActionIcon = createLink(ActionIconWrapper);
export const ActionIconLink: LinkComponent<typeof ActionIconWrapper> = (props) => (
  <RouterActionIcon preload="intent" {...props} />
);

const CardWrapper = forwardRef<HTMLAnchorElement, AsAnchor<CardProps>>((props, ref) => (
  <Card component="a" ref={ref} {...props} />
));
const RouterCard = createLink(CardWrapper);
export const CardLink: LinkComponent<typeof CardWrapper> = (props) => (
  <RouterCard preload="intent" {...props} />
);

const NavLinkWrapper = forwardRef<HTMLAnchorElement, AsAnchor<NavLinkProps>>((props, ref) => (
  <NavLink component="a" ref={ref} {...props} />
));
const RouterNavLink = createLink(NavLinkWrapper);
/** Active links get data-active from the router (activeProps), which NavLink styles. */
export const NavLinkLink: LinkComponent<typeof NavLinkWrapper> = (props) => (
  <RouterNavLink preload="intent" activeProps={{ 'data-active': true }} {...props} />
);
